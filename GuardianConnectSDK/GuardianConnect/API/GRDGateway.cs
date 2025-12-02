using System.Net;
using GuardianConnect.Credentials;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
//using Newtonsoft.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GuardianConnect.Abstractions;
using GuardianConnect.API.Model;
using Microsoft.Extensions.Logging;

namespace GuardianConnect.API;

public class GRDGateway
{
    private Microsoft.Extensions.Logging.ILogger<GRDGateway> _logger;

    public GRDGateway()
    {
        _logger = StaticLoggerFactory.CreateLogger<GRDGateway>();
        _logger.LogInformation("GRDGateway logger!");
    }

    public class RegisterDevicePayload
    {
        [JsonPropertyName("subscriber-credential")]
        public string subscriberCredential { get; set; } = string.Empty;

        [JsonPropertyName("transport-protocol")] public string transportProtocol { get; set; } = string.Empty;
    }

    public class InvalidateCredsPayload
    {
        [JsonPropertyName("apitoken")]
        public string ApiToken { get; set; } = string.Empty;
        
        [JsonPropertyName("subscribercredential")]
        public string SubscriberCredential { get; set; } = string.Empty;
    }

    public string ApiHostname => GRDVPNHelper.Singleton.mainCredential?.HostName ?? string.Empty;

    public string ApiAuthToken => GRDVPNHelper.Singleton.mainCredential?.ApiAuthToken ?? string.Empty;

    public string DeviceIdentifier
    {
        get
        {
            var mainCreds = GRDVPNHelper.Singleton.mainCredential;
            if (mainCreds is { TransportProtocol: ITransportProvider.TransportProtocol.TransportIKEv2 })
            {
                return mainCreds.UserName;
            }

            return mainCreds?.ClientId ?? string.Empty;
        }
    }

    public string BaseHostName => ApiHostname;

    public bool CanMakeApiRequests
    {
        get => !string.IsNullOrEmpty(BaseHostName);
    }

    internal HttpRequestMessage RequestWithEndpoint(string apiEndpoint, string requestData)
    {
        // TJE - do we need this?
        Uri reqUri = new Uri($"https://{BaseHostName}{apiEndpoint}");
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, reqUri);
        HttpContent content = new StringContent(requestData);
        request.Content = content;

        return request;
    }

    /// endpoint: /vpnsrv/api/server-status
    /// hits the endpoint for the current VPN host to check if a VPN connection can be established
    public async Task<ErrorResponse> GetServerStatus(string hostOverride, bool clientCall = false)
    {
        var vpnHost = hostOverride;
        ErrorResponse errorResponse = new ErrorResponse();
        HttpResponseMessage response = new HttpResponseMessage();
        _logger.LogInformation(
            "In GetServerStatus. Called from Guardian Firewall "
            + (clientCall ? "Client CONN#12" : "Service Power Resume"));

        if (clientCall && CanMakeApiRequests == false)
        {
            HttpResponseMessage errorMessage = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            errorResponse.SetResponse(errorMessage).SetErrorMessage("Can not make API requests at this time.");
            return errorResponse;
        }

        _logger.LogInformation($"GetServerStatus: Making status call to host {vpnHost} ...");
        Uri reqUri = new Uri($"https://{vpnHost}/vpnsrv/api/server-status");
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, reqUri);

        try
        {
            response = await HttpUtils.Client.SendAsync(request);
        }
        catch (Exception e)
        {
            // Let's be a little quiet if calling from Guardian Firewall Service - that's only
            // done during Power Resume for polling the network stack readiness. We don't want
            // to flood the log with Exception stacks when we know we're looping on failure until
            // TCP/IP network stack is settled.
            
            if (clientCall) _logger.LogError(e, "Exception thrown in GetServerStatus on server status");
            errorResponse.SetException(e);
            return errorResponse;
        }

        errorResponse.SetResponse(response);
        _logger.LogInformation($"GetServerStatus: returning response: {errorResponse.Message}");
        return errorResponse;
    }

    /// endpoint: /vpnsrv/api/server-status
    /// hits the endpoint for the current VPN host to check if a VPN connection can be established
    /// This signature of method uses host from main credentials in GRDVPNHelper
    /// and calls dual-use (service/client) version that takes host parameter
    public async Task<ErrorResponse> GetServerStatus()
    {
        var vpnHost = ApiHostname;
        var t = await GetServerStatus(vpnHost, true);

        return t;
    }


    #region v1.3 APIs

    /// Used to register a new device for a given transport protocol
    /// @param transportProtocol Specified what kind of VPN credentials will be returned
    /// @param hostname The hostname of the VPN node
    /// @param subscriberCredential The Subscriber Credential which should be used to authenticate
    /// @param validFor The amount of days the VPN credentials should be valid for
    /// @param options Optional non-standard values which should be passed to the VPN node via the JSON body of the request
    /// @param completion The completion handler called once the task is compeleted
    public async Task<ErrorResponse> RegisterDeviceForTransportProtocol(
        ITransportProvider.TransportProtocol transportProtocol, string hostname, string subscriberCredentialJWT,
        int validForDays)
    {
        var errorResponse = new ErrorResponse();
        var response = new HttpResponseMessage();
        var credsList = new List<GRDCredential>();
        
        // CONN#10
        _logger.LogInformation("CONN#10: RegisterDeviceForTransportProtocol()");

        RegisterDevicePayload payload = new RegisterDevicePayload()
        {
            subscriberCredential = subscriberCredentialJWT,
            //transportProtocol = ITransportProvider.TransportProtocol.TransportIKEv2.ToString()
            transportProtocol = "ikev2"
        };

        Uri reqUri = new Uri($"https://{hostname}/api/v1.3/device");
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, reqUri);
        string payLoadString = JsonSerializer.Serialize(payload, RegisterDevicePayloadJsonContext.Default.RegisterDevicePayload);
        _logger.LogInformation($"RegisterDeviceForTransportProtocol: payload for call is '{payLoadString}");
        request.Content = new StringContent(payLoadString);

        try
        {
            response = await HttpUtils.Client.SendAsync(request);
            errorResponse.SetResponse(response).SetData(new List<GRDCredential>());
            string respContent = await response.Content.ReadAsStringAsync();
            var cred = JsonSerializer.Deserialize<GRDCredential>(respContent, GRDCredentialJsonContext.Default.GRDCredential);
            _logger.LogInformation($"RegisterDeviceForTransportProtocol: resp Status={response.StatusCode}, cred values: ApiAuthToken: {cred.ApiAuthToken}, ClientId: {cred.ClientId}, DevicePrivateKey: {cred.DevicePrivateKey}, DevicePublicKey: {cred.DevicePublicKey}, Ipv4Address: {cred.IPv4Address}");
            if (cred != null) credsList.Add(cred);
        }
        catch (Exception e)
        {
            errorResponse.SetException(e);
        }

        errorResponse.SetData(credsList);
        // TJE - TODO - CHECK WITH CJ AS TO WHY WE AREN'T CHECKING 

        return errorResponse;
    }

    public async Task<ErrorResponse> InvalidateCredentialsForClientId(string clientId, string apiToken, string hostName, string subCred)
    {
        ErrorResponse errorResponse = new ErrorResponse();
        HttpResponseMessage response = new HttpResponseMessage();
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(apiToken) || string.IsNullOrEmpty(hostName))
        {
            _logger.LogInformation("Unable to call hosts to invalidate credentials as identifier portions are null or empty.");
            return new ErrorResponse("EMPTYPARAMS", null, true, null);
        }

        InvalidateCredsPayload payload = new InvalidateCredsPayload { ApiToken = apiToken, SubscriberCredential = subCred };

        Uri reqUri = new Uri($"https://{hostName}/api/v1.3/device/{clientId}/invalidate-credentials");
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, reqUri);

        try
        {
            response = await HttpUtils.Client.SendAsync(request);
            errorResponse.SetResponse(response);

        }
        catch (Exception e)
        {
            errorResponse.SetException(e).SetErrorMessage("ERROR").SetResponse(response);
        }

        return errorResponse;
    }

    #region Device Filter Configs

    // Upon success will return a NSDictionary containing four booleans
    //- (void)getDeviceFitlerConfigsForDeviceId:(NSString * _Nonnull)deviceId apiToken:(NSString * _Nonnull)apiToken completion:(void(^)(NSDictionary * _Nullable configFilters, NSError * _Nullable errorMessage))completion;

    // Will run through all keys and values in configFilters and send them to the VPN node
    //- (void)setDeviceFilterConfigsForDeviceId:(NSString * _Nonnull)deviceId apiToken:(NSString * _Nonnull)apiToken deviceConfigFilters:(NSDictionary * _Nonnull)configFilters completion:(void(^)(NSError * _Nullable errorMessage))completion;

    public Task<int> GetDeviceFilterConfigsForDeviceId()
    {
        throw new NotImplementedException();
    }

    public async void SetDeviceFilterConfigsForDeviceId()
    {
        if (!GRDVPNHelper.Singleton.IsConnected(out _)) return;
        if (string.IsNullOrEmpty(BaseHostName))
        {
            _logger.LogError("Cannot set DeviceFilterConfig since BaseHostName is not set!");
            return;
        }
        
        // Get DeviceFilterConfig object
        var dfcCurrent = GRDVPNHelper.Singleton.CurrentDeviceBlocklistConfig;
        dfcCurrent.Api_auth_token =  ApiAuthToken;
        // TJE 102225: Check and set our CurrentDeviceBlockListConfig's Api-Auth-Token value from MainCredentials
        _logger.LogInformation("SetDeviceFilterConfigsForDevice: Updating CurrentDeviceBlocklistConfig api_auth_token");
        GRDVPNHelper.Singleton.CurrentDeviceBlocklistConfig.Api_auth_token = ApiAuthToken;
        //
        var dfcJson = JsonSerializer.Serialize(dfcCurrent, DeviceFilterConfigJsonContext.Default.DeviceFilterConfig);
        //var clientId = GRDCredentialManager.MainCredentials.ClientId;
        var clientId = DeviceIdentifier;

        // build request
        Uri reqUri = new Uri($"https://{BaseHostName}/api/v1.3/device/{clientId}/config/filters");
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, reqUri);
        request.Content = new StringContent(dfcJson);

        HttpResponseMessage response = await HttpUtils.Client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError($"SetDeviceFilterConfigsForDeviceId: Error returned when syncing with host: {response.StatusCode}");
        }
        else
        {
            _logger.LogInformation($"SetDeviceFilterConfigsForDeviceId: Syncing with host successful: {response.StatusCode}");
        }
    }

#endregion
    #endregion
    
    #region NOTYET
    
#if NOTYET
/// endpoint: /api/v1.1/register-and-create
/// @param subscriberCredential JWT token obtained from housekeeping
/// @param validFor integer informing the API how long the EAP credentials should be valid for. A value of 30 indicated 30 days starting right now (eg. 30 days * 24 hours worth of service)
/// @param completion completion block indicating success, returning EAP Credentials as well as an API auth token or returning an error message for user consumption
- (void)registerAndCreateWithSubscriberCredential:(NSString *_Nonnull)subscriberCredential validForDays:(NSInteger)validFor completion:(void (^)(NSDictionary * _Nullable credentials, BOOL success, NSString * _Nullable errorMessage))completion;

/// endpoint: /api/v1.1/register-and-create
/// @param hostname The host we are creating the credential for
/// @param subscriberCredential JWT token obtained from housekeeping
/// @param validFor integer informing the API how long the EAP credentials should be valid for. A value of 30 indicated 30 days starting right now (eg. 30 days * 24 hours worth of service)
/// @param completion completion block indicating success, returning EAP Credentials as well as an API auth token or returning an error message for user consumption
- (void)registerAndCreateWithHostname:(NSString *_Nonnull)hostname subscriberCredential:(NSString *_Nonnull)subscriberCredential validForDays:(NSInteger)validFor completion:(void (^)(NSDictionary * _Nullable, BOOL, NSString * _Nullable))completion;

/// endpoint: /api/v1.2/device/<eap-username>/verify-credentials
/// Validates the existence of the current actively used EAP credentials with the VPN server. If a VPN server has been reset or the EAP credentials have been invalided and/or deleted the app needs to migrate to a new host and obtain new EAP credentials
/// A Subscriber Crednetial is required to prevent broad abuse of the endpoint, thought it is not required to provide the same Subscriber Credential which was initially used to generate the EAP credentials in the past. Any valid Subscriber Credential will be accepted
- (void)verifyEAPCredentialsUsername:(NSString * _Nonnull)eapUsername apiToken:(NSString * _Nonnull)apiToken andSubscriberCredential:(NSString * _Nonnull)subscriberCredential forVPNNode:(NSString * _Nonnull)vpnNode completion:(void(^)(BOOL success, BOOL stillValid, NSString * _Nullable errorMessage, BOOL subCredInvalid))completion;

/// endpoint: /api/v1.2/device/<eap-username>/invalidate-credentials
/// @param eapUsername the EAP username to invalidate. Also used as the device ID
/// @param apiToken the API token for the EAP username to invalidate
/// @param completion completion block indicating a successfull API call or returning an error message
- (void)invalidateEAPCredentials:(NSString *_Nonnull)eapUsername andAPIToken:(NSString *_Nonnull)apiToken completion:(void (^)(BOOL success, NSString * _Nullable errorMessage))completion;

/// endpoint: /api/v1.2/device/<eap-username>/invalidate-credentials
/// @param credentials GRDCredentials to invalidate
/// @param completion completion block indicating a successfull API call or returning an error message
- (void)invalidateEAPCredentials:(GRDCredential *_Nonnull)credentials completion:(void (^)(BOOL, NSString * _Nullable))completion;

/// Used to verify that the local credentials are still valid and can be used to establish the VPN connection again
/// @param clientId The client id assosicated with the VPN credentials
/// @param apiToken The API token to authenticate the request
/// @param hostname The hostname of the VPN node
/// @param subCred The Subscriber Credential to authenticate the request and prevent connection spoofing
/// @param completion The completion handler called once the task is completed
- (void)verifyCredentialsForClientId:(NSString *)clientId withAPIToken:(NSString *)apiToken hostname:(NSString * _Nonnull)hostname subscriberCredential:(NSString * _Nonnull)subCred completion:(void (^)(BOOL success, BOOL credentialsValid, NSString * _Nullable errorMessage))completion;

#endif

#if NOTYET // TJE TODO ?
/// endpoint: /api/v1.1/device/<eap-username>/alerts
/// @param completion De-Serialized JSON from the server containing an array with all alerts
- (void)getEvents:(void (^)(NSDictionary *response, BOOL success, NSString *_Nullable error))completion;

/// endpoint: /api/v1.2/device/<eap-username>/set-alerts-download-timestamp
/// @param completion completion block indicating a successful API request or an error message with detailed information
- (void)setAlertsDownloadTimestampWithCompletion:(void(^)(BOOL success, NSString * _Nullable errorMessage))completion;

/// endpoint: /api/v1.2/device/<eap-username>/alert-totals
/// @param completion completion block indicating a successful API request, if successful a dictionary with the alert totals per alert category or an error message
- (void)getAlertTotals:(void (^)(NSDictionary * _Nullable alertTotals, BOOL success, NSString * _Nullable errorMessage))completion;


/// endpoint: /api/v1.1/<device_token>/set-push-token
/// @param pushToken APNS push token sent to VPN server
/// @param dataTrackers indicator whether or not to send push notifications for data trackers
/// @param locationTrackers indicator whether or not to send push notifications for location trackers
/// @param pageHijackers indicator whether or not to send push notifications for page hijackers
/// @param mailTrackers indicator whether or not to send push notifications for mail trackers
/// @param completion completion block indicating success, and an error message with information for the user
- (void)setPushToken:(NSString *_Nonnull)pushToken andDataTrackersEnabled:(BOOL)dataTrackers locationTrackersEnabled:(BOOL)locationTrackers pageHijackersEnabled:(BOOL)pageHijackers mailTrackersEnabled:(BOOL)mailTrackers completion:(void (^)(BOOL success, NSString * _Nullable errorMessage))completion;

/// endpoint: /api/v1.1/device/<device_token>/remove-push-token
/// @param completion completion block indicating success, and an error message with information for the user
- (void)removePushTokenWithCompletion:(void (^)(BOOL success, NSString * _Nullable errorMessage))completion;

#endif
    #endregion
}