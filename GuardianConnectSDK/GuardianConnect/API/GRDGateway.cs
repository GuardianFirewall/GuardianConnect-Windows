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
            // ClientId is populated symmetrically by GRDCredential.InitWithTransportProtocol
            // (IKEv2 copies UserName into ClientId; WG sets it from the public key exchange response).
            // No protocol special-case needed.
            return GRDCredentialManager.GetMainCredentials()?.ClientId ?? string.Empty;
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
        // DELIBERATELY still v1.3: server-status is absent from the v1.4 spec
        // (GRD-1461 gateway-team question #1). The doc warns mixed API
        // versions are "possible but not guaranteed" — move this to v1.4 (or
        // get the mix blessed) before the migration ships.
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

    // WireGuardRegistrationResponse retired: both protocols' registration
    // replies deserialize into the shared VPNDeviceResponse DTO
    // (GuardianConnect.Credentials), which is carried verbatim on the
    // credential. The v1.4 spec states the registration response is unchanged
    // from previous API versions, so the DTO carries over as-is.

    #region SGW APIs (v1.4 migration, GRD-1461)

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
        // WireGuard requires a curve25519 public-key in the request and parses
        // a different response shape (server-public-key, mapped-ipv4, etc.).
        // Dispatch to the WG-specific implementation; otherwise fall through
        // to the IKEv2 registration below. Previously the WG branch silently
        // sent the IKEv2-shaped payload and returned an incomplete credential
        // — that worked only because no caller actually used this method's WG
        // result (StartWireGuardConnectionWithKeyExchange called Establish
        // WireGuardCredential directly). Now ConnectVpnWithNewUserCredentials
        // ForProtocol(WG) routes through this method too, so it must work
        // symmetrically for both protocols.
        if (transportProtocol == GRDTransportProtocol.TransportProtocol.TransportWireGuard)
        {
            return await EstablishWireGuardCredential(hostname, subscriberCredentialJWT, validForDays);
        }

        var errorResponse = new ErrorResponse();

        var payload = new RegisterDevicePayload
        {
            subscriberCredential = subscriberCredentialJWT,
            transportProtocol = GRDTransportProtocol.TransportProtocolStringFor(transportProtocol)
        };

        // v1.4 (GRD-1461): registration moved to the renamed
        // /device-credentials path; request keys and response shape are
        // unchanged from v1.3.
        var reqUri = new Uri($"https://{hostname}/api/v1.4/device-credentials");
        var request = new HttpRequestMessage(HttpMethod.Post, reqUri);
        var payLoadString =
            JsonSerializer.Serialize(payload, RegisterDevicePayloadJsonContext.Default.RegisterDevicePayload);
        Logger.LogInformation($"RegisterDeviceForTransportProtocol: payload for call is '{payLoadString}");
        request.Content = new StringContent(payLoadString);

        try
        {
            var response = await HttpUtils.Client.SendAsync(request);
            errorResponse.SetResponse(response);
            var respContent = await response.Content.ReadAsStringAsync();

            // v1.4 standardizes every non-2xx body as
            // {"error-title","error-message"}, explicitly user-showable.
            // Parse it so callers get a presentable GRDAPIError instead of a
            // raw body dump (FromResponseBody degrades gracefully for
            // anything non-standard, e.g. proxy HTML).
            if (!response.IsSuccessStatusCode)
            {
                var apiError = GRDAPIError.FromResponseBody(respContent, response.StatusCode);
                Logger.LogError(
                    "RegisterDeviceForTransportProtocol: device registration failed {Status}: {Title}: {Message}",
                    (int)response.StatusCode, apiError.Title, apiError.Message);
                return errorResponse.SetGrdApiError(apiError);
            }

            // Same VPNDeviceResponse DTO as the WireGuard branch — for IKEv2 the
            // host fills eap-username / eap-password / api-auth-token. The factory
            // plucks the fields this protocol needs (and sets ClientId = EAP user).
            var device = JsonSerializer.Deserialize(
                respContent, GRDCredentialJsonContext.Default.VPNDeviceResponse);
            if (device is null)
                return errorResponse.SetErrorMessage(
                    "Device registration response could not be parsed.");

            var cred = GRDCredential.CreateFromDeviceResponse(
                transportProtocol, device,
                hostName: hostname, hostnameDisplayValue: hostname,
                mainCredential: true, validForDays: validForDays);
            errorResponse.SetData(cred);
        }
        catch (Exception e)
        {
            errorResponse.SetException(e);
        }

        return errorResponse;
    }

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
    public static async Task<ErrorResponse> EstablishWireGuardCredential(
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
            Logger.LogError(ex, "EstablishWireGuardCredential: curve25519 keygen failed");
            return errorResponse.SetException(ex);
        }

        var payload = new RegisterDevicePayload
        {
            subscriberCredential = subscriberCredentialJWT,
            transportProtocol    = GRDTransportProtocol.TransportProtocolStringFor(GRDTransportProtocol.TransportProtocol.TransportWireGuard),
            PublicKey            = publicKey.ToBase64()
        };

        // v1.4 (GRD-1461): renamed /device-credentials path; same keys/response.
        var reqUri = new Uri($"https://{hostname}/api/v1.4/device-credentials");
        var request = new HttpRequestMessage(HttpMethod.Post, reqUri);
        var payloadString = JsonSerializer.Serialize(
            payload, RegisterDevicePayloadJsonContext.Default.RegisterDevicePayload);
        // Don't log the JWT or public key — both are sensitive. Log shape only.
        Logger.LogInformation(
            "EstablishWireGuardCredential: POST /api/v1.4/device-credentials transport=wireguard host={Host}",
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
        VPNDeviceResponse? wgResponse;
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
                    // v1.4 standardized error body — parse for a
                    // user-showable title/message (see IKEv2 branch).
                    var body = await response.Content.ReadAsStringAsync();
                    var apiError = GRDAPIError.FromResponseBody(body, response.StatusCode);
                    Logger.LogError(
                        "EstablishWireGuardCredential: register failed {Status}: {Title}: {Message}",
                        (int)response.StatusCode, apiError.Title, apiError.Message);
                    return errorResponse.SetGrdApiError(apiError);
                }

                var respContent = await response.Content.ReadAsStringAsync();
                wgResponse = JsonSerializer.Deserialize(
                    respContent, GRDCredentialJsonContext.Default.VPNDeviceResponse);
                break;
            }
            catch (Exception e) when (IsTransientDnsOrNetworkFailure(e) && attempt < maxAttempts)
            {
                Logger.LogWarning(
                    "EstablishWireGuardCredential: transient DNS/network failure on attempt {Attempt}/{Max} for host '{Host}'; retrying after 350ms. {Msg}",
                    attempt, maxAttempts, hostname, e.Message);
                lastException = e;
                await Task.Delay(350);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "EstablishWireGuardCredential: HTTP/parse failure");
                return errorResponse.SetException(e);
            }
        }
        if (lastException is not null)
        {
            Logger.LogInformation(
                "EstablishWireGuardCredential: retry succeeded for host '{Host}' after transient failure '{Msg}'",
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

        // Carry the host reply verbatim on the credential; the client-side
        // keypair (private key never leaves this process) is passed in
        // separately. No UserName/Password stuffing — the WG field set is
        // disjoint from IKEv2's by construction now.
        var credential = GRDCredential.CreateFromDeviceResponse(
            GRDTransportProtocol.TransportProtocol.TransportWireGuard,
            wgResponse,
            hostName: hostname,
            hostnameDisplayValue: hostname,
            mainCredential: true,
            validForDays: validForDays,
            devicePrivateKey: privateKey.ToBase64(),
            devicePublicKey: publicKey.ToBase64());

        errorResponse.SetData(credential);
        Logger.LogInformation(
            "EstablishWireGuardCredential: success — clientId={ClientId}, ipv4={IPv4}",
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

        // v1.4 (GRD-1461): same path shape under the new version. The body
        // already matches the v1.4 spec exactly (api-auth-token +
        // subscriber-credential). Best-effort semantics per the spec — the
        // response body can be dismissed.
        var reqUri = new Uri($"https://{hostName}/api/v1.4/device/{clientId}/invalidate-credentials");
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

    // Why Task (not void): an async void here re-throws unobserved exceptions
    // onto the calling SynchronizationContext (Avalonia UI dispatcher), which
    // crashed the app when BaseHostName failed DNS during region churn.
    public static async Task SetDeviceFilterConfigsForDeviceId()
    {
        if (!GRDVPNHelper.Singleton.IsConnected(out _)) return;
        if (string.IsNullOrEmpty(BaseHostName))
        {
            Logger.LogError("Cannot set DeviceFilterConfig since BaseHostName is not set!");
            return;
        }

        try
        {
            // Get DeviceFilterConfig object
            var dfcCurrent = GRDVPNHelper.Singleton.CurrentDeviceBlocklistConfig;
            if (dfcCurrent != null) dfcCurrent.Api_auth_token = ApiAuthToken;

            Logger.LogInformation("SetDeviceFilterConfigsForDevice: Updating CurrentDeviceBlocklistConfig api_auth_token");
            if (GRDVPNHelper.Singleton.CurrentDeviceBlocklistConfig != null)
                GRDVPNHelper.Singleton.CurrentDeviceBlocklistConfig.Api_auth_token = ApiAuthToken;

            var dfcJson = JsonSerializer.Serialize(dfcCurrent, DeviceFilterConfigJsonContext.Default.DeviceFilterConfig);
            var clientId = DeviceIdentifier;

            // build request
            // DELIBERATELY still v1.3: the standalone filters endpoint is
            // absent from the v1.4 spec — registration now takes
            // device-filter-config inline, but WE change filters mid-session
            // from the UI (GRD-1461 gateway-team question #2). Migrate or
            // redesign once the gateway team answers.
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
        catch (Exception ex)
        {
            Logger.LogError($"SetDeviceFilterConfigsForDeviceId: Exception during sync: {ex.GetType().Name}: {ex.Message}");
        }
    }

    #endregion - Device Filter Configs

    #endregion - SGW APIs (v1.4 migration, GRD-1461)
}