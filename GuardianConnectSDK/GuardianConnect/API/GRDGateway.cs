using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using GuardianConnect.Abstractions;
using GuardianConnect.API.Model;
using GuardianConnect.Credentials;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GuardianConnect.API;

public class GRDGateway
{
    private static ILogger _logger = NullLogger.Instance;

    private static ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance)
            {
                _logger = StaticLoggerFactory.CreateLogger("GRDGateway");
                _logger.LogInformation("GRDGateway: TEST Log");
            }

            return _logger;
        }
    }

    public static string ApiHostname => GRDCredentialManager.GetMainCredentials()?.HostName ?? string.Empty;

    public static string ApiAuthToken => GRDCredentialManager.GetMainCredentials()?.ApiAuthToken ?? string.Empty;

    public static string DeviceIdentifier
    {
        get
        {
            var mainCreds = GRDCredentialManager.GetMainCredentials();
            if (mainCreds is { TransportProtocol: ITransportProvider.TransportProtocol.TransportIKEv2 })
                return mainCreds.UserName;

            return mainCreds?.ClientId ?? string.Empty;
        }
    }

    public static string BaseHostName => ApiHostname;

    public static bool CanMakeApiRequests => !string.IsNullOrEmpty(BaseHostName);

    public static HttpRequestMessage RequestWithEndpoint(string apiEndpoint, string requestData)
    {
        var reqUri = new Uri($"https://{BaseHostName}{apiEndpoint}");
        var request = new HttpRequestMessage(HttpMethod.Post, reqUri);
        HttpContent content = new StringContent(requestData);
        request.Content = content;

        return request;
    }

    /// endpoint: /vpnsrv/api/server-status
    /// hits the endpoint for the current VPN host to check if a VPN connection can be established
    public static async Task<ErrorResponse> GetServerStatus(string hostOverride, bool clientCall = false)
    {
        var vpnHost = hostOverride;
        var errorResponse = new ErrorResponse();
        var response = new HttpResponseMessage();
        Logger.LogInformation(
            "In GetServerStatus. Called from Guardian Firewall "
            + (clientCall ? "Client Connection step" : "Service Power Resume"));

        if (clientCall && !CanMakeApiRequests)
        {
            var errorMessage = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            errorResponse.SetResponse(errorMessage).SetErrorMessage("Can not make API requests at this time.");
            return errorResponse;
        }

        Logger.LogInformation($"GetServerStatus: Making status call to host {vpnHost} ...");
        var reqUri = new Uri($"https://{vpnHost}/vpnsrv/api/server-status");
        var request = new HttpRequestMessage(HttpMethod.Get, reqUri);

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

            if (clientCall) Logger.LogError(e, "Exception thrown in GetServerStatus on server status");
            errorResponse.SetException(e);
            return errorResponse;
        }

        errorResponse.SetResponse(response);
        Logger.LogInformation($"GetServerStatus: returning response: {errorResponse.Message}");
        return errorResponse;
    }

    /// endpoint: /vpnsrv/api/server-status
    /// hits the endpoint for the current VPN host to check if a VPN connection can be established
    /// This signature of method uses host from main credentials in GRDVPNHelper
    /// and calls dual-use (service/client) version that takes host parameter
    public static async Task<ErrorResponse> GetServerStatus()
    {
        var vpnHost = ApiHostname;
        var t = await GetServerStatus(vpnHost, true);

        return t;
    }

    public class RegisterDevicePayload
    {
        [JsonPropertyName("subscriber-credential")]
        public string subscriberCredential { get; set; } = string.Empty;

        [JsonPropertyName("transport-protocol")]
        public string transportProtocol { get; set; } = string.Empty;
    }


    #region v1.3 APIs

    /// Used to register a new device for a given transport protocol
    /// @param transportProtocol Specified what kind of VPN credentials will be returned
    /// @param hostname The hostname of the VPN node
    /// @param subscriberCredential The Subscriber Credential which should be used to authenticate
    /// @param validFor The amount of days the VPN credentials should be valid for
    /// @param options Optional non-standard values which should be passed to the VPN node via the JSON body of the request
    /// @param completion The completion handler called once the task is compeleted
    public static async Task<ErrorResponse> RegisterDeviceForTransportProtocol(
        ITransportProvider.TransportProtocol transportProtocol, string hostname, string subscriberCredentialJWT,
        int validForDays)
    {
        var errorResponse = new ErrorResponse();
        var response = new HttpResponseMessage();
        var credsList = new List<GRDCredential>();

        var payload = new RegisterDevicePayload
        {
            subscriberCredential = subscriberCredentialJWT,
            //transportProtocol = ITransportProvider.TransportProtocol.TransportIKEv2.ToString()
            transportProtocol = "ikev2"
        };

        var reqUri = new Uri($"https://{hostname}/api/v1.3/device");
        var request = new HttpRequestMessage(HttpMethod.Post, reqUri);
        var payLoadString =
            JsonSerializer.Serialize(payload, RegisterDevicePayloadJsonContext.Default.RegisterDevicePayload);
        Logger.LogInformation($"RegisterDeviceForTransportProtocol: payload for call is '{payLoadString}");
        request.Content = new StringContent(payLoadString);

        try
        {
            response = await HttpUtils.Client.SendAsync(request);
            errorResponse.SetResponse(response).SetData(new List<GRDCredential>());
            var respContent = await response.Content.ReadAsStringAsync();
            var cred = JsonSerializer.Deserialize<GRDCredential>(respContent,
                GRDCredentialJsonContext.Default.GRDCredential);
            // 0.40.1 - settings ClientId from EapUser if IKEv2
            if (cred != null && cred.TransportProtocol == ITransportProvider.TransportProtocol.TransportIKEv2)
                cred.ClientId = cred.UserName;
            if (cred != null)
            {
                Logger.LogInformation(
                    $"RegisterDeviceForTransportProtocol: resp Status={response.StatusCode}, cred values: ApiAuthToken: {cred.ApiAuthToken}, ClientId: {cred.ClientId}, DevicePrivateKey: {cred.DevicePrivateKey}, DevicePublicKey: {cred.DevicePublicKey}, Ipv4Address: {cred.IPv4Address}");
                credsList.Add(cred);
            }
        }
        catch (Exception e)
        {
            errorResponse.SetException(e);
        }

        errorResponse.SetData(credsList);

        return errorResponse;
    }

    public static async Task<ErrorResponse> InvalidateCredentialsForClientId(string clientId, string apiToken,
        string hostName, string subCred)
    {
        var errorResponse = new ErrorResponse();
        var response = new HttpResponseMessage();
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(apiToken) || string.IsNullOrEmpty(hostName))
        {
            Logger.LogInformation(
                "Unable to call hosts to invalidate credentials as identifier portions are null or empty.");
            return new ErrorResponse("EMPTYPARAMS", null, true);
        }

        var payload = new InvalidateCredsPayload { ApiToken = apiToken, SubscriberCredential = subCred };

        var reqUri = new Uri($"https://{hostName}/api/v1.3/device/{clientId}/invalidate-credentials");
        var request = new HttpRequestMessage(HttpMethod.Post, reqUri);
        request.Content = new StringContent(JsonSerializer.Serialize(payload,
            InvalidateCredsPayloadJsonContext.Default.InvalidateCredsPayload));

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

    public static Task<int> GetDeviceFilterConfigsForDeviceId()
    {
        throw new NotImplementedException();
    }

    public static async void SetDeviceFilterConfigsForDeviceId()
    {
        if (!GRDVPNHelper.Singleton.IsConnected(out _)) return;
        if (string.IsNullOrEmpty(BaseHostName))
        {
            Logger.LogError("Cannot set DeviceFilterConfig since BaseHostName is not set!");
            return;
        }

        // Get DeviceFilterConfig object
        var dfcCurrent = GRDVPNHelper.Singleton.CurrentDeviceBlocklistConfig;
        if (dfcCurrent != null) dfcCurrent.Api_auth_token = ApiAuthToken;

        Logger.LogInformation("SetDeviceFilterConfigsForDevice: Updating CurrentDeviceBlocklistConfig api_auth_token");
        if (GRDVPNHelper.Singleton.CurrentDeviceBlocklistConfig != null)
            GRDVPNHelper.Singleton.CurrentDeviceBlocklistConfig.Api_auth_token = ApiAuthToken;

        var dfcJson = JsonSerializer.Serialize(dfcCurrent, DeviceFilterConfigJsonContext.Default.DeviceFilterConfig);
        var clientId = DeviceIdentifier;

        // build request
        var reqUri = new Uri($"https://{BaseHostName}/api/v1.3/device/{clientId}/config/filters");
        var request = new HttpRequestMessage(HttpMethod.Post, reqUri);
        request.Content = new StringContent(dfcJson);

        var response = await HttpUtils.Client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
            Logger.LogError(
                $"SetDeviceFilterConfigsForDeviceId: Error returned when syncing with host: {response.StatusCode}");
        else
            Logger.LogInformation(
                $"SetDeviceFilterConfigsForDeviceId: Syncing with host successful: {response.StatusCode}");
    }

    #endregion - Device Filter Configs

    #endregion - v1.3 APIs
}