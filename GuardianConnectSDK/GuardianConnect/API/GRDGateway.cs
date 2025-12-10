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

    public string ApiHostname => GRDCredentialManager.GetMainCredentials()?.HostName ?? string.Empty;

    public string ApiAuthToken => GRDCredentialManager.GetMainCredentials()?.ApiAuthToken ?? string.Empty;

    public string DeviceIdentifier
    {
        get
        {
            var mainCreds = GRDCredentialManager.GetMainCredentials();
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
        ITransportProvider.TransportProtocol transportProtocol, string hostname, string subscriberCredentialJWT, int validForDays)
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

        _logger.LogInformation("SetDeviceFilterConfigsForDevice: Updating CurrentDeviceBlocklistConfig api_auth_token");
        GRDVPNHelper.Singleton.CurrentDeviceBlocklistConfig.Api_auth_token = ApiAuthToken;

        var dfcJson = JsonSerializer.Serialize(dfcCurrent, DeviceFilterConfigJsonContext.Default.DeviceFilterConfig);
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

    #endregion - Device Filter Configs
    #endregion - v1.3 APIs
    
}