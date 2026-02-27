using GuardianConnect.API;
using GuardianConnect.API.Model;
using GuardianConnect.Credentials;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Win32.System.Power;

namespace GuardianConnect;

public static class GRDHousekeepingAPI
{
    private static Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;
    private static Microsoft.Extensions.Logging.ILogger Logger
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

    public static GrdUserLoginResponse LoginResponse { get; set; } = new GrdUserLoginResponse();
    public static GRDSubscriberCredential? LiveGrdCredential { get; set; }

    /// endpoint: /api/v1/users/info-for-pe-token
    /// @param token password equivalent token for which to request information for
    /// @param completion completion block returning NSDictionary with information for the requested token, an error message and a bool indicating success of the request
    public static async Task<ErrorResponse> RequestPETokenInformationForToken(string peToken)
    {
        ErrorResponse errorResponse = new ErrorResponse();
        
        // Validation Method PEToken...
        if (string.IsNullOrEmpty(peToken))
        {
            Logger.LogError("RequestPETokenInformationForToken: PEToken is null or empty");

            return errorResponse
                .SetException(new ArgumentNullException("NO PE Token Provided"))
                .SetErrorMessage("No pe token provided");
        }

        Uri uri = new Uri($"https://{Common.kConnectAPIHostname}/api/v1/users/info-for-pe-token");
        try
        {
            var pet = new PeTokenRequest("pe-token", peToken);
            HttpContent content = new StringContent(JsonSerializer.Serialize(pet, GRDPETokenJsonContext.Default.GRDPEToken));
            content.Headers.Remove("Content-Type");
            content.Headers.Add("Content-Type", "application/json; charset=utf-8");
            HttpResponseMessage response = await HttpUtils.Client.PostAsync(uri, content);
            if (response.IsSuccessStatusCode)
            {
                string result = await response.Content.ReadAsStringAsync();
                PeTokenResponse peTokenResponse = JsonSerializer.Deserialize<PeTokenResponse>(result, PeTokenResponseJsonContext.Default.PeTokenResponse) ?? new PeTokenResponse();
                errorResponse.SetData(peTokenResponse).SetResponse(response);
            }
            else
            {
                // ... check response values...
                int statusCode = (int)response.StatusCode;
                if (statusCode == 500)
                {
                    Logger.LogError(@"Housekeeping failed to return subscriber credential");
                    errorResponse.SetResponse(response).SetErrorMessage("500 - Internal Server Error");
                }
                if (statusCode == 400)
                {
                    Logger.LogError(@"Failed to create subscriber credential. Faulty input values");
                    errorResponse.SetResponse(response).SetErrorMessage("400 - Failed to create subscriber credential. Faulty input values");
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
        // CONN#9
        Logger.LogInformation("CONN#9");
        // TJE: Called by GRDVPNHelper.GetValidSubscriberCredentialWithCompletion()
        
        // set host to use
        string connectHost = GRDVPNHelper.Singleton.PeToken?.ConnectAPIEnv ?? Common.kConnectAPIHostname;
        // Validation Method PEToken...
        string peToken = GRDVPNHelper.Singleton.PeToken?.Token;
        if (string.IsNullOrEmpty(peToken))
        {
            Logger.LogError(@"PEToken Object has empty token. Trying string from keychain...");
            peToken = GRDKeychain.GetPasswordStringForAccount(IGRDKeychain.kKeychainStr_PEToken_Itself);
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

        PeTokenRequest petRequest = new PeTokenRequest(peToken);

        // TJE: ASK CJ ABOUT THIS! [022526] - everywhere else in this class we call into our own backend server
        // but here we decide whether to call into our own backend server or an external API
        // Is that because pe-token can be set from us or a partner?
        Uri uri = new Uri($"https://{connectHost}/api/v1.2/subscriber-credential/create");
        try
        {
            var pet = new PeTokenRequest("pe-token", petRequest.PeToken);
            
            string serializedPetReq = JsonSerializer.Serialize(pet, PeTokenRequestJsonContext.Default.PeTokenRequest);
            Logger.LogInformation($"CreateSubscriberCredentialForBundleId: serializedPetReq = '{serializedPetReq}'");
            HttpContent content = new StringContent(serializedPetReq);
            content.Headers.Remove("Content-Type");
            content.Headers.Add("Content-Type", "application/json; charset=utf-8");
            HttpResponseMessage response = await HttpUtils.Client.PostAsync(uri, content);
            if (response.IsSuccessStatusCode)
            {
                string result = await response.Content.ReadAsStringAsync();
                var jwt = JsonSerializer.Deserialize<GrdSubscriberCredentialJwt>(result, GRDSubScriberCredentialJwtJsonContext.Default.GrdSubscriberCredentialJwt);
                LiveGrdCredential = new GRDSubscriberCredential(jwt!.SubscriberCredential!);
                LiveGrdCredential.Store();
                Logger.LogInformation("CreateSubscriberCredentialForBundleId(): JWT obtained.");
            }
            else
            {
                Logger.LogInformation("CreateSubscriberCredentialForBundleId(): Error occurred.");
                // ... check response values...
                int statusCode = (int)response.StatusCode;
                if (statusCode == 500)
                {
                    var message = "Housekeeping failed to return subscriber credential";
                    Logger.LogInformation(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;

                }
                else if (statusCode == 400)
                {
                    var message = "Failed to create subscriber credential. Faulty input values";
                    Logger.LogInformation(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;
                }
                else if (statusCode == 401)
                {
                    var message = "No subscription present";
                    Logger.LogInformation(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;

                }
                else if (statusCode == 410)
                {
                    // Not sending an error message back so that we're not showing a useless error to the user
                    // The app should transition to free/unpaid if required
                    var message = "Subscription expired";
                    Logger.LogInformation(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;

                }
                else if (statusCode == 402) // Payment required
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
            errorResponse = new ErrorResponse(ex.Message, ex, true, null);
            return errorResponse;
        }
        //
        errorResponse = new ErrorResponse(string.Empty, null, false, string.Empty)
        {
            Data = LiveGrdCredential.Jwt
        };
        return errorResponse;
    }

    internal static async Task<ErrorResponse> RequestLatestTimeZonesForRegions()
    {
        const string GetTimeZonesForRegionsUrl = $"https://{Common.kConnectAPIHostname}/api/v1.1/servers/timezones-for-regions";

        ErrorResponse errorResponse = new ErrorResponse();
        Uri uri = new Uri(GetTimeZonesForRegionsUrl);
        try
        {
            HttpResponseMessage response = await HttpUtils.Client.GetAsync(uri);
            if (response.IsSuccessStatusCode)
            {
                string result = await response.Content.ReadAsStringAsync();
                errorResponse.SetData(result);
            }
            else
            {
                int statusCode = (int)response.StatusCode;
                Logger.LogError($"RequestLatestTimeZonesForRegions: Call to url '{uri.AbsoluteUri}' failed with status code {statusCode}");
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
        string GetAllRegionsUrl = $"https://{Common.kConnectAPIHostname}/api/v1/servers/all-server-regions";
        ErrorResponse errorResponse = new ErrorResponse();
        Uri uri = new Uri(GetAllRegionsUrl);
        try
        {
            Logger.LogInformation("RequestServerRegions: Getting latest Regions collection from backend...");
            {
                HttpResponseMessage response = HttpUtils.Client.GetAsync(uri).GetAwaiter().GetResult(); // Task short-circuit jump
                if (response.IsSuccessStatusCode)
                {
                    Logger.LogInformation($"RequestServerRegions: Return from getting regions: Response statusCode = {response.StatusCode}");
                    string content = await response.Content.ReadAsStringAsync(); // Task short-circuit jump
                    if (string.IsNullOrEmpty(content))
                    {
                        Logger.LogInformation("RequestServerRegions: content returned for regions is empty");
                        errorResponse.SetErrorMessage("Content returned for regions is empty").SetResponse(response).SetData(null).IsError = true;
                    }
                    else
                    {
                        errorResponse.SetData(content).SetResponse(response);
                    }
                }
                else
                {
                    Logger.LogInformation($"RequestServerRegions: Response from attempting to get latest regions is {response.StatusCode}");
                    errorResponse.SetErrorMessage("Content returned for regions is empty").SetResponse(response).SetData(null).IsError = true;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"RequestServerRegions(): Exception thrown when calling all-server-regions...: {ex.Message}. (STATIC) Using GRDRegion.StaticRegions list data");
            errorResponse.SetException(ex);
        }

        return errorResponse;
    }
    
    #region GRDConnectionSubscriber/Device calls
    /* Api calls:
        @"/api/v1.2/partners/subscribers/new"];
        @"/api/v1.2/partners/subscriber/device-reference"];
        @"/api/v1.2/partners/subscriber/update"];
        @"/api/v1.2/partners/subscriber/validate"];
        @"/api/v1.2/partners/subscriber/logout"];
     */
    
    // [#190 - called by #170] - DONE
    public static async Task<ErrorResponse> AccountCreationStateAsync(string identifier, string secret)
    {
        var errorResponse = new ErrorResponse();
        var body = new Dictionary<string, object>
        {
            [GRDConnectDevice.kGuardianDeviceSubscriberIdentifierKey] = identifier,
            [GRDConnectDevice.kGuardianDeviceSubscriberSecretKey] = secret
        };
        
        using var content = JsonContent.Create(body);
        content.Headers.TryAddWithoutValidation("GRD-Connect-Publishable-Key", "<partner-app-publishable-key>");
        
        var uri = MakeUri("/api/v1.2/partners/subscriber/devices/delete");
        HttpResponseMessage response = HttpUtils.Client.PostAsync(uri, content).GetAwaiter().GetResult();
        var data = String.Empty;
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
        if (response.IsSuccessStatusCode) return errorResponse;
        errorResponse.SetGrdApiError(dict, response.StatusCode);
        return errorResponse;
    }

    //  [#191 called by #179] - DONE
    public static async Task<(Dictionary<string, object>, ErrorResponse)> AddConnectDeviceAsync(string peToken, string nickname, string acceptedTOS)
    {
        var errorResponse = new ErrorResponse();
        
        var body = new Dictionary<string, object?>
        {
            [GRDConnectDevice.kGuardianConnectDevicePETokenKey] = peToken,
            [GRDConnectDevice.kGuardianConnectDeviceNicknameKey] = nickname,
            [GRDConnectDevice.kGuardianConnectDeviceAcceptedTOSKey] = true
        };

        using var content = JsonContent.Create(body);
        content.Headers.TryAddWithoutValidation("GRD-Connect-Publishable-Key", "<partner-app-publishable-key>");
        
        var uri = MakeUri("/api/v1.2/partners/subscriber/devices/add");
        HttpResponseMessage response = HttpUtils.Client.PostAsync(uri, content).GetAwaiter().GetResult();
        string data = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.InternalServerError) data = string.Empty;
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
        if (response.IsSuccessStatusCode) return (dict, errorResponse);
        errorResponse.SetGrdApiError(dict, response.StatusCode);
        return (new Dictionary<string, object?>(), errorResponse);
    }
    
    // [#192 used by #180] - DONE
    public static async Task<(Dictionary<string, object>, ErrorResponse)> UpdateConnectDeviceAsync(string peToken,
        string nickname, string uuid)
    {
        var errorResponse = new ErrorResponse();

        var body = new Dictionary<string, object?>
        {
            [GRDConnectDevice.kGuardianConnectDevicePETokenKey] = peToken,
            [GRDConnectDevice.kGuardianConnectDeviceNicknameKey] = nickname,
            [GRDConnectDevice.kGuardianConnectDeviceUUIDKey] = uuid
        };

        using var content = JsonContent.Create(body);
        content.Headers.TryAddWithoutValidation("GRD-Connect-Publishable-Key", "<partner-app-publishable-key>");

        var uri = MakeUri("/api/v1.2/partners/subscriber/devices/update");
        HttpResponseMessage response = HttpUtils.Client.PostAsync(uri, content).GetAwaiter().GetResult();
        string data = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.InternalServerError) data = string.Empty;
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
        if (response.IsSuccessStatusCode) return (dict, errorResponse);
        errorResponse.SetGrdApiError(dict, response.StatusCode);
        return (new Dictionary<string, object?>(), errorResponse);
    }

    // [#193] - called from [#167] - indentifier/secret or [#181] - peToken - DONE
    internal static async Task<(List<Dictionary<string, object>>, ErrorResponse)>
        RequestAllConnectDevicesForSubscriberAsync(string peToken, string identifier, string secret)
    {
        var errorResponse = new ErrorResponse();
        var body = new Dictionary<string, object>();
        
        if (peToken != null) body.Add(GRDConnectDevice.kGuardianConnectDevicePETokenKey, peToken);
        else
        {
            body.Add(GRDConnectDevice.kGuardianDeviceSubscriberIdentifierKey, identifier);
            body.Add(GRDConnectDevice.kGuardianDeviceSubscriberSecretKey, secret);
        }
        
        using var content = JsonContent.Create(body);
        content.Headers.TryAddWithoutValidation("GRD-Connect-Publishable-Key", "<partner-app-publishable-key>");
        
        var uri = MakeUri("/api/v1.2/partners/subscriber/devices/list");
        HttpResponseMessage response = HttpUtils.Client.PostAsync(uri, content).GetAwaiter().GetResult();
        string data = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.InternalServerError) data = string.Empty;
        if (! response.IsSuccessStatusCode)
        {
            var errorDict = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
            errorResponse.SetGrdApiError(errorDict, response.StatusCode);
            return (new List<Dictionary<string, object>>() , errorResponse);
        }
        
        var list = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(data);
        return (list, errorResponse);
    }

    // [#194] Delete Device - sub-issue of [#179] - DONE
    public static async Task<ErrorResponse> DeleteConnectDeviceAsync(string peToken, string identifier, string secret)
    {
        var errorResponse = new ErrorResponse();
        var body = new Dictionary<string, object>();
        
        if (peToken != null) body.Add(GRDConnectDevice.kGuardianConnectDevicePETokenKey, peToken);
        else
        {
            body.Add(GRDConnectDevice.kGuardianDeviceSubscriberIdentifierKey, identifier);
            body.Add(GRDConnectDevice.kGuardianDeviceSubscriberSecretKey, secret);
        }
        
        using var content = JsonContent.Create(body);
        content.Headers.TryAddWithoutValidation("GRD-Connect-Publishable-Key", "<partner-app-publishable-key>");
        
        var uri = MakeUri("/api/v1.2/partners/subscriber/devices/delete");
        HttpResponseMessage response = HttpUtils.Client.PostAsync(uri, content).GetAwaiter().GetResult();
        if (response.IsSuccessStatusCode) return errorResponse;
        var data = string.Empty;
        if (response.StatusCode == HttpStatusCode.InternalServerError) data = string.Empty;
        var errorDict = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
        errorResponse.SetGrdApiError(errorDict, response.StatusCode);

        return errorResponse;
    }
    
    // [#195] Validate Device - sub-issue of [#183] - DONE
    public static async Task<ErrorResponse> ValidateConnectDeviceAsync(string peToken)
    {
        var errorResponse = new ErrorResponse();
        var body = new Dictionary<string, object>
        {
            [GRDConnectDevice.kGuardianConnectDevicePETokenKey] = peToken
        };
        
        using var content = JsonContent.Create(body);
        content.Headers.TryAddWithoutValidation("GRD-Connect-Publishable-Key", "<partner-app-publishable-key>");

        var uri = MakeUri("/api/v1.2/partners/subscriber/devices/validate");
        HttpResponseMessage response = HttpUtils.Client.PostAsync(uri, content).GetAwaiter().GetResult();
        string data = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.InternalServerError) data = string.Empty;
        if (response.IsSuccessStatusCode) return errorResponse;
        var errorDict = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
        errorResponse.SetGrdApiError(errorDict, response.StatusCode);
        return errorResponse;
    }
    
    //----------------------------------
    
    // [#185 called by #169]
    public static async Task<(Dictionary<string, object>,  ErrorResponse)> CallHostToAddPartnersNewSubscriberAsync(ref GRDConnectSubscriber subscriber)
    {
        var errorResponse = new ErrorResponse();

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(body)
        };

        request.Headers.TryAddWithoutValidation("GRD-Connect-Publishable-Key", "<partner-app-publishable-key>");

        var response = await httpClient.SendAsync(request);
        
        
        
        var suffix = "/api/v1.3/partners/subscribers/new #185";
        var ValidateConnectDeviceUrl = $"https://{Common.kConnectAPIHostname}{suffix}";
        
        
        var body = new Dictionary<string, object?>
        {
            [GRDConnectDevice.kGuardianConnectDevicePETokenKey] = peToken,
            [GRDConnectDevice.kGuardianConnectDeviceNicknameKey] = nickname,
            [GRDConnectDevice.kGuardianConnectDeviceAcceptedTOSKey] = true
        };

        using var content = JsonContent.Create(body);
        
        var uri = MakeUri("/api/v1.2/partners/subscriber/devices/add");
        HttpResponseMessage response = HttpUtils.Client.PostAsync(uri, content).GetAwaiter().GetResult();
        string data = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.InternalServerError) data = string.Empty;
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(data);
        if (! response.IsSuccessStatusCode)
        {
            errorResponse.SetGrdApiError(dict, response.StatusCode);
            return (new Dictionary<string, object?>(), errorResponse);
        }
        return (dict, errorResponse);
    }


    // [#186]
    public static Task<ErrorResponse> CallHostToGetPartnersSubscriberDeviceReferenceAsync(ref GRDConnectSubscriber subscriber)
    {
        var errorResponse = new ErrorResponse();
        var suffix = "/api/v1.2/partners/subscribers/device-reference";
        var ValidateConnectDeviceUrl = $"https://{Common.kConnectAPIHostname}{suffix}";
        
        Uri uri = new Uri(ValidateConnectDeviceUrl);
        HttpContent content = new StringContent("{ petoken: " + peToken + " }");
        content.Headers.Remove("Content-Type");
        content.Headers.Add("Content-Type", "application/json; charset=utf-8");
        HttpResponseMessage response = HttpUtils.Client.PostAsync(uri, content).GetAwaiter().GetResult();
        errorResponse.IsError = (response.IsSuccessStatusCode);
        errorResponse.SetResponse(response);
        return Task.FromResult(errorResponse); 
        
    }

    // [#187]
    public static Task<ErrorResponse> CallHostToUpdatePartnerSubscriberAsync(ref GRDConnectSubscriber subscriber)
    {
        var errorResponse = new ErrorResponse();
        var suffix = "/api/v1.2/partners/subscribers/update";
        var UpdateConnectSubscriberUrl = $"https://{Common.kConnectAPIHostname}{suffix}";
        
        Uri uri = new Uri(UpdateConnectSubscriberUrl);
        HttpContent content = new StringContent("{ petoken: " + peToken + " }");
        content.Headers.Remove("Content-Type");
        content.Headers.Add("Content-Type", "application/json; charset=utf-8");
        HttpResponseMessage response = HttpUtils.Client.PostAsync(uri, content).GetAwaiter().GetResult();
        errorResponse.IsError = (response.IsSuccessStatusCode);
        errorResponse.SetResponse(response);
        return Task.FromResult(errorResponse); 
        
    }

    // [#188]
    public static Task<ErrorResponse> CallHostToValidatePartnerSubscriberAsync(ref GRDConnectSubscriber subscriber)
    {
        var errorResponse = new ErrorResponse();
        var suffix = "/api/v1.2/partners/subscribers/validate";
        var ValidateConnectDeviceUrl = $"https://{Common.kConnectAPIHostname}{suffix}";
        
        Uri uri = new Uri(ValidateConnectDeviceUrl);
        HttpContent content = new StringContent("{ petoken: " + peToken + " }");
        content.Headers.Remove("Content-Type");
        content.Headers.Add("Content-Type", "application/json; charset=utf-8");
        HttpResponseMessage response = HttpUtils.Client.PostAsync(uri, content).GetAwaiter().GetResult();
        errorResponse.IsError = (response.IsSuccessStatusCode);
        errorResponse.SetResponse(response);
        return Task.FromResult(errorResponse); 
        
    }

    // [#189]
    public static Task<ErrorResponse> CallHostToLogoutPartnerSubscriberAsync(ref GRDConnectSubscriber subscriber)
    {
        var errorResponse = new ErrorResponse();
        var suffix = "/api/v1.2/partners/subscribers/logout";
        var LogoutUrl = $"https://{Common.kConnectAPIHostname}{suffix}";
        
        Uri uri = new Uri(LogoutUrl);
        HttpContent content = new StringContent("{ petoken: " + peToken + " }");
        content.Headers.Remove("Content-Type");
        content.Headers.Add("Content-Type", "application/json; charset=utf-8");
        HttpResponseMessage response = HttpUtils.Client.PostAsync(uri, content).GetAwaiter().GetResult();
        errorResponse.IsError = (response.IsSuccessStatusCode);
        errorResponse.SetResponse(response);
        return Task.FromResult(errorResponse); 
        
    }
    #endregion GRDConnectionSubscriber/Device calls
    
    private static Uri MakeUri(string path)
    {
        return new Uri($"https://{Common.kConnectAPIHostname}{path}");
    }
}