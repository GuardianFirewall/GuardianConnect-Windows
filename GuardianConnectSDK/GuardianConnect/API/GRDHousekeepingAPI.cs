using GuardianConnect.API;
using GuardianConnect.API.Model;
using GuardianConnect.Credentials;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Net.Http.Headers;
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

        // TJE - remove comment - taken from AuthenticateUser.cs in UI
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
    /// <summary>
    /// Create a function called allDevices which calls GRDConnectDevice currentDevice in order have a reference to itself as well
    /// as the API endpoint /api/v1.2/partners/subscriber/devices/list endpoint in order to get the complete list of devices
    /// associated with the current Connect Subscriber
    ///
    /// Upon successful response from the API the JSON response data needs to be parsed in to GRDConnectDevice objects.
    /// While parsing the JSON response data from the API endpoint the GRDConnectDevice object that is currently being
    /// processed should be compared to the GRDConnectDevice reference return from the currentDevice function call.
    /// The current device can be matched by comparing the uuid on both objects and if they match the currentDevice boolean should be set.
    /// 
    /// </summary>
    /// <param name="identifier"></param>
    /// <param name="secret"></param>
    /// <returns>List<GRDConnectDevice></returns>
    internal static async Task<(List<GRDConnectDevice>, ErrorResponse)> RequestAllConnectDevicesForSubscriberAsync(ConnectDeviceRequestData requestParameters)
    {
        ErrorResponse errorResponse = new ErrorResponse();
        
        var devices = new List<GRDConnectDevice>();
        const string GetAllDevicesForSubscriberUrl = $"https://{Common.kConnectAPIHostname}/api/v1.2/partners/subscriber/devices/list";
        Uri uri = new Uri(GetAllDevicesForSubscriberUrl);

        HttpContent content = null;
        if (requestParameters.PeToken != null)
        {
            content = new StringContent(JsonSerializer.Serialize(requestParameters.PeToken,
                GRDPETokenJsonContext.Default.GRDPEToken));
        }
        else
        {
            content = new StringContent(JsonSerializer.Serialize(requestParameters, ConnectDeviceRequestDataJsonContext.Default.ConnectDeviceRequestData));
        }

        content.Headers.Remove("Content-Type");
        content.Headers.Add("Content-Type", "application/json; charset=utf-8");
        HttpResponseMessage response = await HttpUtils.Client.PostAsync(uri, content);
        try
        {
            if (response.IsSuccessStatusCode)
            {
                string result = await response.Content.ReadAsStringAsync();
                devices = JsonSerializer.Deserialize<List<GRDConnectDevice>>(result, GRDConnectDeviceJsonContext.Default.ListGRDConnectDevice);
                errorResponse.SetData(devices).SetResponse(response);
            }
            else
            {
                int statusCode = (int)response.StatusCode;
                Logger.LogError($"RequestAllConnectDevicesForSubscriberAsync: Call to url '{uri.AbsoluteUri}' failed with status code {statusCode}");
                errorResponse.SetResponse(response).SetErrorMessage($"Failed with status code {statusCode}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Exception thrown - RequestLatestTimeZonesForRegions: {ex.Message}");
            errorResponse.SetException(ex);
        }

        return (devices, errorResponse);
    }

    /// <summary>
    /// Add a function which accepts the following function parameters
    /// peToken type: string
    /// nickname type: string
    /// acceptedTOS type: string
    /// The function should start by checking that the function parameters of type string are non-empty strings as well
    /// as that the parameter acceptedTOS is true.
    ///
    /// It should then call the API endpoint /api/v1.2/partners/subscriber/devices/add and parse the response data into
    /// a GRDConnectDevice object by calling the initiWithDictionary function and finally return the parsed connect
    /// device object
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    public static Task<ErrorResponse> CallHostToAddConnectDeviceAsync(ConnectDeviceRequestData request)
    {
        var AddConnectDeviceUrl = $"https://{Common.kConnectAPIHostname}/api/v1.2/partners/subscriber/devices/add";
        var errorResponse = new ErrorResponse();
        
        Uri uri = new Uri(AddConnectDeviceUrl);
        HttpContent content = new StringContent(JsonSerializer.Serialize(request, ConnectDeviceRequestDataJsonContext.Default.ConnectDeviceRequestData));
        content.Headers.Remove("Content-Type");
        content.Headers.Add("Content-Type", "application/json; charset=utf-8");
        HttpResponseMessage response = HttpUtils.Client.PostAsync(uri, content).GetAwaiter().GetResult();
        errorResponse.SetResponse(response);
        return Task.FromResult(errorResponse);
    }

    /// <summary>
    /// Add a function called updateConnectDevice which accepts the following function parameters
    ///     peToken type: string
    ///     nickname type: string
    /// The function should call the API endpoint /api/v1.2/partners/subscriber/devices/update and parse the response
    /// data into a GRDConnectDevice object by calling the initFromDictionary function
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    public static Task<ErrorResponse> CallHostToUpdateConnectDeviceAsync(ConnectDeviceRequestData request)
    {
        var UpdateConnectDeviceUrl = $"https://{Common.kConnectAPIHostname}/api/v1.2/partners/subscriber/devices/add";
        var errorResponse = new ErrorResponse();
        
        Uri uri = new Uri(UpdateConnectDeviceUrl);
        HttpContent content = new StringContent(JsonSerializer.Serialize(request, ConnectDeviceRequestDataJsonContext.Default.ConnectDeviceRequestData));
        content.Headers.Remove("Content-Type");
        content.Headers.Add("Content-Type", "application/json; charset=utf-8");
        HttpResponseMessage response = HttpUtils.Client.PostAsync(uri, content).GetAwaiter().GetResult();
        errorResponse.SetResponse(response);
        return Task.FromResult(errorResponse); 
    }

    public static Task<ErrorResponse> CallHostToValidateConnectDeviceAsync(string peToken)
    {
        var ValidateConnectDeviceUrl = $"https://{Common.kConnectAPIHostname}/api/v1.2/partners/subscriber/device/validate";
        var errorResponse = new ErrorResponse();
        
        Uri uri = new Uri(ValidateConnectDeviceUrl);
        HttpContent content = new StringContent("{ petoken: " + peToken + " }");
        content.Headers.Remove("Content-Type");
        content.Headers.Add("Content-Type", "application/json; charset=utf-8");
        HttpResponseMessage response = HttpUtils.Client.PostAsync(uri, content).GetAwaiter().GetResult();
        errorResponse.SetResponse(response);
        return Task.FromResult(errorResponse); 
        
    }

    #endregion GRDConnectionSubscriber/Device calls
}