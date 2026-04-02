using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GuardianConnect.API.Model;
using GuardianConnect.Credentials;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GuardianConnect;

public static class GRDHousekeepingAPI
{
    private static ILogger _logger = NullLogger.Instance;

    private static ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance)
            {
                _logger = StaticLoggerFactory.CreateLogger("GRDHousekeepingAPI");
                _logger.LogInformation("GRDHousekeepingAPI: TEST Log");
            }

            return _logger;
        }
    }

    private static string ConnectAPIHostname =>
        GRDKeychain.ReadRegistryData(Common.kConnectAPIHostname)
        ?? Common.DefaultConnectAPIHostname;

    private static string HousekeepingAPIHostname =>
        GRDKeychain.ReadRegistryData(Common.kHousekeepingAPIHostname)
        ?? Common.DefaultHousekeepingAPIHostname;

    public static string PublishableKey => GRDKeychain.ReadRegistryData("TESTVALUE_CS_PublishableKey");

    public static GrdUserLoginResponse LoginResponse { get; set; } = new();
    public static GRDSubscriberCredential? LiveGrdCredential { get; set; }

    /// endpoint: /api/v1/users/info-for-pe-token
    /// @param token password equivalent token for which to request information for
    /// @param completion completion block returning NSDictionary with information for the requested token, an error message and a bool indicating success of the request
    public static async Task<ErrorResponse> RequestPETokenInformationForToken(string peToken)
    {
        var errorResponse = new ErrorResponse();

        // Validation Method PEToken...
        if (string.IsNullOrEmpty(peToken))
        {
            Logger.LogError("RequestPETokenInformationForToken: PEToken is null or empty");

            return errorResponse
                .SetException(new ArgumentNullException("NO PE Token Provided"))
                .SetErrorMessage("No pe token provided");
        }

        // TODO - MATCH CONCISE CODE IN CONNECTSUBSCRIBER/DEVICE METHODS
        var uri = new Uri($"https://{Common.DefaultConnectAPIHostname}/api/v1/users/info-for-pe-token");
        try
        {
            var pet = new PeTokenRequest("pe-token", peToken);
            HttpContent content =
                new StringContent(JsonSerializer.Serialize(pet, GRDPETokenJsonContext.Default.GRDPEToken));
            content.Headers.Remove("Content-Type");
            content.Headers.Add("Content-Type", "application/json; charset=utf-8");
            var response = await HttpUtils.Client.PostAsync(uri, content);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                var peTokenResponse =
                    JsonSerializer.Deserialize<PeTokenResponse>(result,
                        PeTokenResponseJsonContext.Default.PeTokenResponse) ?? new PeTokenResponse();
                errorResponse.SetData(peTokenResponse).SetResponse(response);
            }
            else
            {
                // ... check response values...
                var statusCode = (int)response.StatusCode;
                if (statusCode == 500)
                {
                    Logger.LogError(@"Housekeeping failed to return subscriber credential");
                    errorResponse.SetResponse(response).SetErrorMessage("500 - Internal Server Error");
                }

                if (statusCode == 400)
                {
                    Logger.LogError(@"Failed to create subscriber credential. Faulty input values");
                    errorResponse.SetResponse(response)
                        .SetErrorMessage("400 - Failed to create subscriber credential. Faulty input values");
                }

                if (statusCode == 401)
                {
                    Logger.LogError(@"No subscription present");
                    errorResponse.SetResponse(response).SetErrorMessage("401 - No subscription present");
                }

                if (statusCode == 410)
                {
                    Logger.LogError(@"Subscription expired");
                    // Not sending an error message back so that we're not showing a useless error to the user
                    // The app should transition to free/unpaid if required
                    errorResponse.SetResponse(response).SetErrorMessage("410 - Subscription expired");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Exception thrown - RequestPETokenInformationForToken: {ex.Message}");
            errorResponse.SetException(ex);
        }
        //

        return errorResponse;
    }

    /// endpoint: /api/v1.2/subscriber-credential/create
    /// Used to obtain a signed JWT from housekeeping for later authentication with zoe-agent
    /// @param validationMethod set to determine how to authenticate with housekeeping
    /// @param dict NSDictionary only used when the 'validationMethod' is set to 'ValidationMethodCustom'
    /// @param completion completion block returning a signed JWT, indicating request success and a user actionable error message if the request failed
    public static async Task<ErrorResponse> CreateSubscriberCredentialForBundleId(string bundleId)
    {
        ErrorResponse errorResponse;

        // set host to use
        var connectHost = GRDVPNHelper.Singleton.PeToken?.ConnectAPIEnv ?? Common.DefaultConnectAPIHostname;
        // Validation Method PEToken...
        var peToken = GRDVPNHelper.Singleton.PeToken?.Token;
        if (string.IsNullOrEmpty(peToken))
        {
            Logger.LogError(@"PEToken Object has empty token. Trying string from keychain...");
            peToken = GRDKeychain.GetPasswordStringForAccount(Common.kKeychainStr_PEToken_Itself);
            if (string.IsNullOrEmpty(peToken))
            {
                Logger.LogError(@"Failed to retrieve PEToken from keychain");
                return new ErrorResponse
                {
                    Data = null,
                    Message = "Failed to retrieve PEToken from keychain",
                    IsError = true
                };
            }
        }

        var petRequest = new PeTokenRequest(peToken);

        var uri = new Uri($"https://{connectHost}/api/v1.2/subscriber-credential/create");
        try
        {
            var pet = new PeTokenRequest("pe-token", petRequest.PeToken);

            var serializedPetReq = JsonSerializer.Serialize(pet, PeTokenRequestJsonContext.Default.PeTokenRequest);
            Logger.LogInformation($"CreateSubscriberCredentialForBundleId: serializedPetReq = '{serializedPetReq}'");
            HttpContent content = new StringContent(serializedPetReq);
            content.Headers.Remove("Content-Type");
            content.Headers.Add("Content-Type", "application/json; charset=utf-8");
            var response = await HttpUtils.Client.PostAsync(uri, content);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                var jwt = JsonSerializer.Deserialize<GrdSubscriberCredentialJwt>(result,
                    GRDSubScriberCredentialJwtJsonContext.Default.GrdSubscriberCredentialJwt);
                LiveGrdCredential = new GRDSubscriberCredential(jwt!.SubscriberCredential!);
                LiveGrdCredential.Store();
                Logger.LogInformation("CreateSubscriberCredentialForBundleId(): JWT obtained.");
            }
            else
            {
                Logger.LogInformation("CreateSubscriberCredentialForBundleId(): Error occurred.");
                // ... check response values...
                var statusCode = (int)response.StatusCode;
                if (statusCode == 500)
                {
                    var message = "Housekeeping failed to return subscriber credential";
                    Logger.LogInformation(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;
                }

                if (statusCode == 400)
                {
                    var message = "Failed to create subscriber credential. Faulty input values";
                    Logger.LogInformation(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;
                }

                if (statusCode == 401)
                {
                    var message = "No subscription present";
                    Logger.LogInformation(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;
                }

                if (statusCode == 410)
                {
                    // Not sending an error message back so that we're not showing a useless error to the user
                    // The app should transition to free/unpaid if required
                    var message = "Subscription expired";
                    Logger.LogInformation(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;
                }

                if (statusCode == 402) // Payment required
                {
                    var message = "Payment required";
                    Logger.LogInformation(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogInformation($"\tERROR {ex.Message}");
            errorResponse = new ErrorResponse(ex.Message, ex, true);
            return errorResponse;
        }

        //
        errorResponse = new ErrorResponse(string.Empty, null, false, string.Empty)
        {
            Data = LiveGrdCredential!.Jwt
        };
        return errorResponse;
    }

    internal static async Task<ErrorResponse> RequestLatestTimeZonesForRegions()
    {
        const string GetTimeZonesForRegionsUrl =
            $"https://{Common.DefaultConnectAPIHostname}/api/v1.1/servers/timezones-for-regions";

        var errorResponse = new ErrorResponse();
        var uri = new Uri(GetTimeZonesForRegionsUrl);
        try
        {
            var response = await HttpUtils.Client.GetAsync(uri);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                errorResponse.SetData(result);
            }
            else
            {
                var statusCode = (int)response.StatusCode;
                Logger.LogError(
                    $"RequestLatestTimeZonesForRegions: Call to url '{uri.AbsoluteUri}' failed with status code {statusCode}");
                errorResponse.SetResponse(response).SetErrorMessage($"Failed with status code {statusCode}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Exception thrown - RequestLatestTimeZonesForRegions: {ex.Message}");
            errorResponse.SetException(ex);
        }

        return errorResponse;
    }

    internal static async Task<ErrorResponse> RequestServerRegions()
    {
        var GetAllRegionsUrl = $"https://{Common.DefaultConnectAPIHostname}/api/v1/servers/all-server-regions";
        var errorResponse = new ErrorResponse();
        var uri = new Uri(GetAllRegionsUrl);
        try
        {
            Logger.LogInformation("RequestServerRegions: Getting latest Regions collection from backend...");
            {
                var response = HttpUtils.Client.GetAsync(uri).GetAwaiter().GetResult(); // Task short-circuit jump
                if (response.IsSuccessStatusCode)
                {
                    Logger.LogInformation(
                        $"RequestServerRegions: Return from getting regions: Response statusCode = {response.StatusCode}");
                    var content = await response.Content.ReadAsStringAsync(); // Task short-circuit jump
                    if (string.IsNullOrEmpty(content))
                    {
                        Logger.LogInformation("RequestServerRegions: content returned for regions is empty");
                        errorResponse.SetErrorMessage("Content returned for regions is empty").SetResponse(response)
                            .SetData(null!).IsError = true;
                    }
                    else
                    {
                        errorResponse.SetData(content).SetResponse(response);
                    }
                }
                else
                {
                    Logger.LogInformation(
                        $"RequestServerRegions: Response from attempting to get latest regions is {response.StatusCode}");
                    errorResponse.SetErrorMessage("Content returned for regions is empty").SetResponse(response)
                        .SetData(null!).IsError = true;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                $"RequestServerRegions(): Exception thrown when calling all-server-regions...: {ex.Message}. (STATIC) Using GRDRegion.StaticRegions list data");
            errorResponse.SetException(ex);
        }

        return errorResponse;
    }

    private static Uri MakeUri(string path)
    {
        return new Uri($"https://{ConnectAPIHostname}{path}", UriKind.RelativeOrAbsolute);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Dictionary body uses known types safe for runtime serialization")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Dictionary body uses known types safe for runtime serialization")]
    private static HttpRequestMessage CreateConnectAPIRequest(string endpoint, Dictionary<string, object?> body,
        string method = "POST")
    {
        var uri = MakeUri(endpoint);
        var httpMethod = HttpMethod.Post;
        switch (method)
        {
            case "PUT": httpMethod = HttpMethod.Put; break;
        }

        var request = new HttpRequestMessage(httpMethod, uri)
        {
            Content = JsonContent.Create(body)
        };

        request.Headers.TryAddWithoutValidation("GRD-Connect-Publishable-Key", PublishableKey);
        return request;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Error dict uses basic types safe for runtime deserialization")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Error dict uses basic types safe for runtime deserialization")]
    private static async Task<ErrorResponse> MakeAPICallAndReturnErrorResponse(string endpoint,
        Dictionary<string, object?> body)
    {
        var errorResponse = new ErrorResponse();

        try
        {
            var request = CreateConnectAPIRequest(endpoint, body);
            var response = await HttpUtils.Client.SendAsync(request);
            var data = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.InternalServerError && string.IsNullOrEmpty(data))
                data = "{}";

            if (response.IsSuccessStatusCode) return errorResponse;
            var errorDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(data);
            errorResponse = errorResponse.SetGrdApiError(errorDict, response.StatusCode);
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"Error making API call to {endpoint}");
        }

        return errorResponse;
    }

    private static async Task<(Dictionary<string, JsonElement>, ErrorResponse)>
        MakeAPICallAndReturnDict(string endpoint, Dictionary<string, object?> body, string method = "POST")
    {
        var errorResponse = new ErrorResponse();
        try
        {
            var request = CreateConnectAPIRequest(endpoint, body, method);
            var response = await HttpUtils.Client.SendAsync(request);
            //var response = await HttpUtils.Client.PutAsync(request);
            var data = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.InternalServerError && string.IsNullOrEmpty(data))
                data = "{}";

            var dict = new Dictionary<string, JsonElement>();
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(data) ? "{}" : data);

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var prop in doc.RootElement.EnumerateObject())
                    dict[prop.Name] = prop.Value.Clone();

            if (response.IsSuccessStatusCode)
                return (dict, errorResponse);

            errorResponse = errorResponse.SetGrdApiError(
                dict.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value),
                response.StatusCode).SetResponse(response);
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"Error making API call to {endpoint}");
        }

        return (new Dictionary<string, JsonElement>(), errorResponse);
    }

    private static async Task<(List<JsonElement>, ErrorResponse)> MakeAPICallAndReturnList(string endpoint,
        Dictionary<string, object?> body)
    {
        var errorResponse = new ErrorResponse();
        try
        {
            var request = CreateConnectAPIRequest(endpoint, body);
            var response = await HttpUtils.Client.SendAsync(request);
            var data = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == HttpStatusCode.InternalServerError && string.IsNullOrEmpty(data))
                data = "[]";

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(data) ? "[]" : data);

            if (response.IsSuccessStatusCode)
            {
                var list = new List<JsonElement>();
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var item in doc.RootElement.EnumerateArray())
                        list.Add(item.Clone());
                return (list, errorResponse);
            }

            var errorDict = doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone())
                : new Dictionary<string, object?>();

            errorResponse = errorResponse.SetGrdApiError(errorDict, response.StatusCode).SetResponse(response);
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"Error making API call to {endpoint}");
        }

        return (new List<JsonElement>(), errorResponse);
    }

    #region GRDConnectionSubscriber/Device calls

    // [#185 called by #169] - TESTED
    public static async Task<(Dictionary<string, JsonElement>, ErrorResponse)>
        AddNewConnectSubscriberAsync(string identifier, string secret, string nickname, string email, bool acceptedTOS)
    {
        var body = new Dictionary<string, object?>
        {
            [Common.kGuardianConnectSubscriberIdentifier] = identifier,
            [Common.kGuardianConnectSubscriberSecret] = secret,
            [Common.kGuardianConnectSubscriberPETNickname] = nickname,
            [Common.kGuardianConnectSubscriberAcceptedTOS] = acceptedTOS,
            [Common.kGuardianConnectSubscriberEmail] = email
        };

        return MakeAPICallAndReturnDict("/api/v1.3/partners/subscribers/new", body).GetAwaiter().GetResult();
    }

    // [#186 - called by #168] - TESTED
    public static async Task<(Dictionary<string, JsonElement>, ErrorResponse)>
        GetDeviceReferenceForConnectSubscriberAsync(string identifier, string secret, string peToken)
    {
        var body = new Dictionary<string, object?>
        {
            [Common.kGuardianConnectSubscriberIdentifier] = identifier,
            [Common.kGuardianConnectSubscriberSecret] = secret,
            [Common.kPETokenKey] = peToken
        };

        var ep = "/api/v1.2/partners/subscriber/device-reference";
        var (jsonResult, errorResponse) = MakeAPICallAndReturnDict(ep, body).GetAwaiter().GetResult();
        return (jsonResult, errorResponse);
    }

    // [#187 - called by #171] - DONE
    public static async Task<(Dictionary<string, JsonElement>, ErrorResponse)> UpdateConnectSubscriberWithEmailAsync(
        string identifier, string secret, string nickname, bool acceptedTOS, string email)
    {
        var body = new Dictionary<string, object?>
        {
            [Common.kGuardianConnectSubscriberIdentifier] = identifier,
            [Common.kGuardianConnectSubscriberSecret] = secret,
            [Common.kGuardianConnectSubscriberPETNickname] = nickname,
            [Common.kGuardianConnectSubscriberAcceptedTOS] = acceptedTOS,
            [Common.kGuardianConnectSubscriberEmail] = email
        };

        var ep = "/api/v1.2/partners/subscriber/update";
        var (dict, errorResponse) = MakeAPICallAndReturnDict(ep, body, "PUT").GetAwaiter().GetResult();
        return (dict, errorResponse);
    }

    // [#188 - called by #172] - DONE
    public static async Task<(Dictionary<string, JsonElement>, ErrorResponse)> ValidateConnectSubscriberAsync(
        string identifier, string secret, string peToken)
    {
        var body = new Dictionary<string, object?>
        {
            [Common.kGuardianConnectSubscriberIdentifier] = identifier,
            [Common.kGuardianConnectSubscriberSecret] = secret,
            [Common.kPETokenKey] = peToken
        };

        var ep = "/api/v1.2/partners/subscriber/validate";
        var (dict, errorResponse) = MakeAPICallAndReturnDict(ep, body).GetAwaiter().GetResult();
        return (dict, errorResponse);
    }

    // [#189 - called by #173] - DONE
    public static async Task<ErrorResponse> LogOutConnectSubscriberAsync(string peToken)
    {
        var errorResponse = new ErrorResponse();
        var body = new Dictionary<string, object?>
        {
            [Common.kPETokenKey] = peToken
        };

        var ep = "/api/v1.2/partners/subscriber/logout";
        return MakeAPICallAndReturnErrorResponse(ep, body).GetAwaiter().GetResult();
    }

    // [#190 - called by #170] - DONE
    public static async Task<ErrorResponse> CheckAccountCreationStateAsync(string identifier, string secret)
    {
        var errorResponse = new ErrorResponse();
        var body = new Dictionary<string, object?>
        {
            [Common.kGuardianConnectSubscriberIdentifier] = identifier,
            [Common.kGuardianConnectSubscriberSecret] = secret
        };

        var ep = "/api/v1.2/partners/subscriber/account-creation-state";
        errorResponse = MakeAPICallAndReturnErrorResponse(ep, body).ConfigureAwait(false).GetAwaiter().GetResult();
        return errorResponse;
    }

    // [#191 called by #179] - DONE
    public static async Task<(Dictionary<string, JsonElement>, ErrorResponse)> AddConnectDeviceAsync(string peToken,
        string nickname, bool acceptedTOS)
    {
        var body = new Dictionary<string, object?>
        {
            [Common.kGuardianConnectDevicePEToken] = peToken,
            [Common.kGuardianConnectDeviceNickname] = nickname,
            [Common.kGuardianConnectDeviceAcceptedTOS] = acceptedTOS
        };

        return MakeAPICallAndReturnDict("/api/v1.2/partners/subscriber/devices/add", body).GetAwaiter().GetResult();
    }

    // [#192 used by #180] - DONE
    public static async Task<(Dictionary<string, JsonElement>, ErrorResponse)> UpdateConnectDeviceAsync(string peToken,
        string nickname, string uuid)
    {
        var body = new Dictionary<string, object?>
        {
            [Common.kGuardianConnectDevicePEToken] = peToken,
            [Common.kGuardianConnectDeviceNickname] = nickname,
            [Common.kGuardianConnectDeviceUUID] = uuid
        };

        var ep = "/api/v1.2/partners/subscriber/device/update";
        var (dict, er) = MakeAPICallAndReturnDict(ep, body, "PUT").GetAwaiter().GetResult();
        return (dict, er);
    }

    // [#193] - called from [#167] - indentifier/secret or [#181] - peToken - DONE
    internal static async Task<(List<JsonElement>, ErrorResponse)>
        RequestAllConnectDevicesForSubscriberAsync(string? peToken = "", string identifier = "", string secret = "")
    {
        var body = new Dictionary<string, object?>();

        if (string.IsNullOrEmpty(peToken))
        {
            body.Add(Common.kGuardianConnectSubscriberIdentifier, identifier);
            body.Add(Common.kGuardianConnectSubscriberSecret, secret);
        }
        else
        {
            body.Add(Common.kGuardianConnectDevicePEToken, peToken);
        }

        var ep = "/api/v1.2/partners/subscriber/devices/list";
        var (list, errorResponse) = MakeAPICallAndReturnList(ep, body).GetAwaiter().GetResult();
        return (list, errorResponse);
    }

    // [#194] Delete Device - sub-issue of [#178] - DONE
    public static async Task<ErrorResponse> DeleteConnectDeviceAsync(string peToken, string identifier, string secret)
    {
        var body = new Dictionary<string, object?>();

        if (!string.IsNullOrEmpty(peToken))
        {
            body.Add(Common.kGuardianConnectDevicePEToken, peToken);
        }
        else
        {
            body.Add(Common.kGuardianDeviceSubscriberIdentifier, identifier);
            body.Add(Common.kGuardianDeviceSubscriberSecret, secret);
        }

        return MakeAPICallAndReturnErrorResponse("/api/v1.2/partners/subscriber/devices/delete", body).GetAwaiter()
            .GetResult();
    }

    // [#195] Validate Device - sub-issue of [#183] - DONE
    public static async Task<(Dictionary<string, JsonElement>, ErrorResponse)> ValidateConnectDeviceAsync(
        string peToken)
    {
        var body = new Dictionary<string, object?>
        {
            [Common.kGuardianConnectDevicePEToken] = peToken
        };


        var (dict, er) = await MakeAPICallAndReturnDict("/api/v1.2/partners/subscriber/device/validate", body);
        return (dict, er);
    }

    #endregion GRDConnectionSubscriber/Device calls
}