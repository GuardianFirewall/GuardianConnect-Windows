using GuardianConnect.API;
using GuardianConnect.API.Model;
using GuardianConnect.Credentials;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

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
                _logger = StaticLoggerFactory.CreateLogger("GRDServerManager");
                _logger.LogInformation("GRDServerManager: TEST Log");
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
            Debug.WriteLine("No pe token provided");
            _logger.LogError("RequestPETokenInformationForToken: PEToken is null or empty");

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
                    Debug.WriteLine(@"Housekeeping failed to return subscriber credential");
                    //if (completion) completion(nil, NO, @"Internal server error - couldn't create subscriber credential");
                    errorResponse.SetResponse(response).SetErrorMessage("500 - Internal Server Error");
                }
                if (statusCode == 400)
                {
                    Debug.WriteLine(@"Failed to create subscriber credential. Faulty input values");
                    //if (completion) completion(nil, NO, @"Failed to create subscriber credential. Faulty input values");
                    errorResponse.SetResponse(response).SetErrorMessage("400 - Failed to create subscriber credential. Faulty input values");
                }
                if (statusCode == 401)
                {
                    Debug.WriteLine(@"No subscription present");
                    //if (completion) completion(nil, NO, @"No subscription present");
                    errorResponse.SetResponse(response).SetErrorMessage("401 - No subscription present");
                }
                if (statusCode == 410)
                {
                    System.Diagnostics.Debug.WriteLine(@"Subscription expired");
                    // Not sending an error message back so that we're not showing a useless error to the user
                    // The app should transition to free/unpaid if required
                    //if (completion) completion(nil, NO, nil);
                    errorResponse.SetResponse(response).SetErrorMessage("410 - Subscription expired");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(@"\tERROR {0}", ex.Message);
            _logger.LogError(ex, $"Exception thrown - RequestPETokenInformationForToken: {ex.Message}");
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
        _logger.LogInformation("CONN#9");
        // TJE: Called by GRDVPNHelper.GetValidSubscriberCredentialWithCompletion()
        
        // set host to use
        string connectHost = GRDVPNHelper.Singleton.PeToken?.ConnectAPIEnv ?? Common.kConnectAPIHostname;
        // Validation Method PEToken...
        string peToken = GRDVPNHelper.Singleton.PeToken?.Token;
        if (string.IsNullOrEmpty(peToken))
        {
            _logger.LogError(@"PEToken Object has empty token. Trying string from keychain...");
            peToken = GRDKeychain.GetPasswordStringForAccount(IGRDKeychain.kKeychainStr_PEToken_Itself);
            if (string.IsNullOrEmpty(peToken))
            {
                _logger.LogError(@"Failed to retrieve PEToken from keychain");
                return new ErrorResponse
                {
                    Data = null,
                    Message = "Failed to retrieve PEToken from keychain",
                    IsError = true
                };
            }
        }

        PeTokenRequest petRequest = new PeTokenRequest(peToken);

        // TJE - remove comment - taken from AuthenticateUser.cs in UI
        Uri uri = new Uri($"https://{connectHost}/api/v1.2/subscriber-credential/create");
        try
        {
            var pet = new PeTokenRequest("pe-token", petRequest.PeToken);
            
            string serializedPetReq = JsonSerializer.Serialize(pet, PeTokenRequestJsonContext.Default.PeTokenRequest);
            _logger.LogInformation($"CreateSubscriberCredentialForBundleId: serializedPetReq = '{serializedPetReq}'");
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
                _logger.LogInformation("CreateSubscriberCredentialForBundleId(): JWT obtained.");
            }
            else
            {
                _logger.LogInformation("CreateSubscriberCredentialForBundleId(): Error occurred.");
                // ... check response values...
                int statusCode = (int)response.StatusCode;
                if (statusCode == 500)
                {
                    var message = "Housekeeping failed to return subscriber credential";
                    _logger.LogInformation(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;

                }
                else if (statusCode == 400)
                {
                    var message = "Failed to create subscriber credential. Faulty input values";
                    _logger.LogInformation(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;
                }
                else if (statusCode == 401)
                {
                    var message = "No subscription present";
                    _logger.LogInformation(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;

                }
                else if (statusCode == 410)
                {
                    // Not sending an error message back so that we're not showing a useless error to the user
                    // The app should transition to free/unpaid if required
                    var message = "Subscription expired";
                    _logger.LogInformation(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;

                }
                else if (statusCode == 402) // Payment required
                {
                    var message = "Payment required";
                    _logger.LogInformation(message);
                    errorResponse = new ErrorResponse(message, null, true, response);
                    return errorResponse;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"\tERROR {ex.Message}");
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
                _logger.LogError($"RequestLatestTimeZonesForRegions: Call to url '{uri.AbsoluteUri}' failed with status code {statusCode}");
                errorResponse.SetResponse(response).SetErrorMessage($"Failed with status code {statusCode}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(@"\tERROR {0}", ex.Message);
            _logger.LogError(ex, $"Exception thrown - RequestLatestTimeZonesForRegions: {ex.Message}");
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
}