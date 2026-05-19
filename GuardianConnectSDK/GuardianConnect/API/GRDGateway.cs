using System.Net;
using System.Net.Sockets;
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

    /// <summary>
    /// True if the given exception chain represents a recoverable DNS or
    /// network connectivity hiccup of the kind that resolves on its own
    /// within a few hundred ms — typically post-WG-teardown when the
    /// Windows resolver hasn't yet failed over from the (now-gone) WG
    /// adapter's DNS to the physical NIC's. Matches HostNotFound /
    /// TryAgain socket errors and broad HttpRequestException with a
    /// SocketException inner cause.
    /// </summary>
    private static bool IsTransientDnsOrNetworkFailure(Exception e)
    {
        // Unwrap one level if it's an HttpRequestException wrapping a SocketException.
        var sockEx = e as SocketException ?? e.InnerException as SocketException;
        if (sockEx is not null)
        {
            return sockEx.SocketErrorCode is
                SocketError.HostNotFound or
                SocketError.TryAgain or
                SocketError.NetworkUnreachable or
                SocketError.HostUnreachable;
        }
        // Some flavours surface only as HttpRequestException with the text
        // in Message — match the canonical Windows messages as a fallback.
        var msg = e.Message;
        return msg.Contains("No such host is known", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase);
    }

    private static ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance)
            {
                _logger = StaticLoggerFactory.CreateLogger("GRDGateway");
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
            if (mainCreds is { TransportProtocol: GRDTransportProtocol.TransportProtocol.TransportIKEv2 })
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

    /// endpoint: /api/v1.3/server-status
    /// hits the endpoint for the current VPN host to check if a VPN connection can be established
    public static async Task<ErrorResponse> GetServerStatus(string hostOverride, bool clientCall = false)
    {
        var vpnHost = hostOverride;
        var errorResponse = new ErrorResponse();
        var response = new HttpResponseMessage();
        Logger.LogInformation("GetServerStatus - " + (clientCall ? "Client Connection step" : "Service Power Resume"));

        if (clientCall && !CanMakeApiRequests)
        {
            var errorMessage = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            errorResponse.SetResponse(errorMessage).SetErrorMessage("Can not make API requests at this time.");
            return errorResponse;
        }

        Logger.LogInformation($"GetServerStatus: Making status call to host {vpnHost} ...");
        var reqUri = new Uri($"https://{vpnHost}/api/v1.3/server-status");
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

    /// endpoint: /api/v1.3/server-status
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

        /// <summary>
        /// WireGuard public-key option (base64). Sent as <c>public-key</c> in the
        /// JSON body for WG registration; absent for IKEv2. Mirrors the
        /// transportOptions dictionary used by the iOS/macOS SDK
        /// (<c>GRDGatewayAPI registerDeviceForTransportProtocol</c>).
        /// </summary>
        [JsonPropertyName("public-key")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? PublicKey { get; set; }
    }

    /// <summary>
    /// Deserialization target for the WireGuard branch of
    /// <c>POST /api/v1.3/device</c>. Kept separate from <see cref="GRDCredential"/>
    /// so adding wire-format JsonPropertyName attrs here doesn't change the
    /// on-disk shape of persisted credentials.
    /// </summary>
    public class WireGuardRegistrationResponse
    {
        [JsonPropertyName("server-public-key")]      public string ServerPublicKey { get; set; } = string.Empty;
        [JsonPropertyName("mapped-ipv4-address")]    public string MappedIPv4Address { get; set; } = string.Empty;
        [JsonPropertyName("mapped-ipv6-address")]    public string MappedIPv6Address { get; set; } = string.Empty;
        [JsonPropertyName("client-id")]              public string ClientId { get; set; } = string.Empty;
        [JsonPropertyName("api-auth-token")]         public string ApiAuthToken { get; set; } = string.Empty;
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
        GRDTransportProtocol.TransportProtocol transportProtocol, string hostname, string subscriberCredentialJWT,
        int validForDays)
    {
        var errorResponse = new ErrorResponse();
        var response = new HttpResponseMessage();
        var credsList = new List<GRDCredential>();

        var payload = new RegisterDevicePayload
        {
            subscriberCredential = subscriberCredentialJWT,
            transportProtocol = TransportProtocolStringFor(transportProtocol)
        };

        var reqUri = new Uri($"https://{hostname}/api/v1.3/device");
        var request = new HttpRequestMessage(HttpMethod.Post, reqUri);
        var payLoadString =
            JsonSerializer.Serialize(payload, RegisterDevicePayloadJsonContext.Default.RegisterDevicePayload);
        Logger.LogInformation($"RegisterDeviceForTransportProtocol: payload for call is '{payLoadString}");
        request.Content = new StringContent(payLoadString);

        GRDCredential cred = new GRDCredential();
        try
        {
            response = await HttpUtils.Client.SendAsync(request);
            errorResponse.SetResponse(response).SetData(new GRDCredential());
            var respContent = await response.Content.ReadAsStringAsync();
            cred = JsonSerializer.Deserialize<GRDCredential>(respContent,
                GRDCredentialJsonContext.Default.GRDCredential);
            // 0.40.1 - sets ClientId from EapUser if IKEv2
            if (cred != null)
            {
                if (cred.TransportProtocol == GRDTransportProtocol.TransportProtocol.TransportIKEv2)
                {
                    cred.ClientId = cred.UserName;
                }
            }
        }
        catch (Exception e)
        {
            errorResponse.SetException(e);
        }

        errorResponse.SetData(cred);

        return errorResponse;
    }

    /// <summary>
    /// Wire-format string used in the <c>transport-protocol</c> JSON field for
    /// <c>POST /api/v1.3/device</c>. Matches the strings the iOS/macOS SDK uses
    /// (<c>GRDTransportProtocol transportProtocolStringFor</c>): "ikev2" or
    /// "wireguard".
    /// </summary>
    public static string TransportProtocolStringFor(GRDTransportProtocol.TransportProtocol protocol) =>
        protocol switch
        {
            GRDTransportProtocol.TransportProtocol.TransportWireGuard => "wireguard",
            _ => "ikev2"
        };

    /// <summary>
    /// Generate a fresh Curve25519 keypair, register the device for the
    /// WireGuard transport at <paramref name="hostname"/>, and populate a
    /// <see cref="GRDCredential"/> with the device private key (kept
    /// client-side), the device public key (echoed back from the server),
    /// and the server-side fields (server public key, mapped IPv4/IPv6,
    /// client id, api auth token).
    ///
    /// Mirrors <c>GRDVPNHelper.createStandaloneCredentialsForTransportProtocol</c>
    /// in the iOS/macOS SDK for the WireGuard branch.
    ///
    /// The returned credential is NOT persisted to the keychain by this
    /// method; callers store via <see cref="GRDCredentialManager.AddOrUpdateCredential"/>
    /// once the higher-level workflow decides it should become the main
    /// credential or a saved alternate.
    /// </summary>
    public static async Task<ErrorResponse> NegotiateWireGuardCredential(
        string hostname, string subscriberCredentialJWT, int validForDays)
    {
        var errorResponse = new ErrorResponse();

        if (string.IsNullOrWhiteSpace(hostname) || string.IsNullOrWhiteSpace(subscriberCredentialJWT))
            return errorResponse.SetErrorMessage("hostname and subscriber JWT are required.");

        // Generate the keypair on-device. The private key never leaves this
        // process; only the public key is sent up.
        Win32Calls.WireGuard.WireGuardKey privateKey;
        Win32Calls.WireGuard.WireGuardKey publicKey;
        try
        {
            privateKey = Win32Calls.WireGuard.WireGuardKey.GeneratePrivateKey();
            publicKey  = privateKey.DerivePublicKey();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "NegotiateWireGuardCredential: curve25519 keygen failed");
            return errorResponse.SetException(ex);
        }

        var payload = new RegisterDevicePayload
        {
            subscriberCredential = subscriberCredentialJWT,
            transportProtocol    = TransportProtocolStringFor(GRDTransportProtocol.TransportProtocol.TransportWireGuard),
            PublicKey            = publicKey.ToBase64()
        };

        var reqUri = new Uri($"https://{hostname}/api/v1.3/device");
        var request = new HttpRequestMessage(HttpMethod.Post, reqUri);
        var payloadString = JsonSerializer.Serialize(
            payload, RegisterDevicePayloadJsonContext.Default.RegisterDevicePayload);
        // Don't log the JWT or public key — both are sensitive. Log shape only.
        Logger.LogInformation(
            "NegotiateWireGuardCredential: POST /api/v1.3/device transport=wireguard host={Host}",
            hostname);
        request.Content = new StringContent(payloadString);

        // SendAsync wrapped in a retry-on-DNS-failure loop. The Windows DNS
        // resolver briefly returns "No such host is known" for a gateway
        // hostname immediately after a previous WG tunnel teardown (the
        // system resolver state hasn't fully recovered from the WG adapter's
        // DNS = 1.1.1.1 having been the active resolver). Service-side
        // we also flush the resolver cache in VpnTunnelManager.StopVPNTunnel;
        // this client-side retry is defense in depth for the residual race.
        // We retry ONCE (after 350ms) on host-not-found / network-unreachable
        // style failures, and surface anything else immediately.
        HttpResponseMessage response;
        WireGuardRegistrationResponse? wgResponse;
        const int maxAttempts = 2;
        int attempt = 0;
        Exception? lastException = null;
        while (true)
        {
            attempt++;
            try
            {
                // SendAsync consumes the HttpRequestMessage on first use; rebuild it on retry.
                if (attempt > 1)
                {
                    request = new HttpRequestMessage(HttpMethod.Post, reqUri)
                    {
                        Content = new StringContent(payloadString),
                    };
                }
                response = await HttpUtils.Client.SendAsync(request);
                errorResponse.SetResponse(response);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    Logger.LogError(
                        "NegotiateWireGuardCredential: register failed {Status}: {Body}",
                        (int)response.StatusCode, body);
                    return errorResponse.SetErrorMessage(
                        $"WireGuard registration failed: {(int)response.StatusCode}");
                }

                var respContent = await response.Content.ReadAsStringAsync();
                wgResponse = JsonSerializer.Deserialize(
                    respContent, WireGuardRegistrationResponseJsonContext.Default.WireGuardRegistrationResponse);
                break;
            }
            catch (Exception e) when (IsTransientDnsOrNetworkFailure(e) && attempt < maxAttempts)
            {
                Logger.LogWarning(
                    "NegotiateWireGuardCredential: transient DNS/network failure on attempt {Attempt}/{Max} for host '{Host}'; retrying after 350ms. {Msg}",
                    attempt, maxAttempts, hostname, e.Message);
                lastException = e;
                await Task.Delay(350);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "NegotiateWireGuardCredential: HTTP/parse failure");
                return errorResponse.SetException(e);
            }
        }
        if (lastException is not null)
        {
            Logger.LogInformation(
                "NegotiateWireGuardCredential: retry succeeded for host '{Host}' after transient failure '{Msg}'",
                hostname, lastException.Message);
        }

        if (wgResponse is null
            || string.IsNullOrEmpty(wgResponse.ServerPublicKey)
            || string.IsNullOrEmpty(wgResponse.MappedIPv4Address)
            || string.IsNullOrEmpty(wgResponse.ClientId))
        {
            return errorResponse.SetErrorMessage(
                "WireGuard registration response missing required fields (server-public-key / mapped-ipv4-address / client-id).");
        }

        var credential = new GRDCredential
        {
            TransportProtocol  = GRDTransportProtocol.TransportProtocol.TransportWireGuard,
            Identifer          = "main",
            MainCredential     = true,
            HostName           = hostname,
            HostnameDisplayValue = hostname,
            Name               = hostname,
            ExpirationDate     = DateTime.UtcNow.AddDays(validForDays),
            ClientId           = wgResponse.ClientId,
            ApiAuthToken       = wgResponse.ApiAuthToken,
            DevicePrivateKey   = privateKey.ToBase64(),
            DevicePublicKey    = publicKey.ToBase64(),
            ServerPublicKey    = wgResponse.ServerPublicKey,
            IPv4Address        = wgResponse.MappedIPv4Address,
            IPv6Address        = wgResponse.MappedIPv6Address,
            // Per CJ pattern from GRDCredential.InitWithTransportProtocol — populate
            // UserName/Password so older code paths that key off them don't break.
            UserName           = wgResponse.ClientId,
            Password           = "wireguard-creds",
        };

        errorResponse.SetData(credential);
        Logger.LogInformation(
            "NegotiateWireGuardCredential: success — clientId={ClientId}, ipv4={IPv4}",
            credential.ClientId, credential.IPv4Address);
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