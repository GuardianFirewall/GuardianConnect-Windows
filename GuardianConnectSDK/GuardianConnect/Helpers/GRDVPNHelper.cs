using System.Diagnostics;
using System.Runtime.CompilerServices;
using GuardianConnect.Abstractions;
using GuardianConnect.API;
using GuardianConnect.API.Model;
using GuardianConnect.Credentials;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Win32Calls;

[assembly: InternalsVisibleTo("GuardianCore")]

namespace GuardianConnect.Helpers;

public class GRDVPNHelper
{
    public enum GRDServerFeatureEnvironment
    {
        ServerFeatureEnvironmentProduction = 1,
        ServerFeatureEnvironmentInternal,
        ServerFeatureEnvironmentDevelopment,
        ServerFeatureEnvironmentDualStack,
        ServerFeatureEnvironmentUnstable
    }

    // Set up a singleton
    private static GRDVPNHelper? _singleton;
    private static ILogger _logger = NullLogger.Instance;

    public readonly GRDServerFeatureEnvironment FeatureEnvironment;

    public readonly bool PreferBetaCapableServers;
    public DeviceFilterConfig? CurrentDeviceBlocklistConfig;

    public GRDPEToken? PeToken;

    protected internal GRDServerFeatureEnvironment? _featureEnvironment;
    private GRDServerManager? _grdServerManager;

    protected internal bool _preferBetaCapableServers;

    /// Set this key/value combinations to authenticate for custom
    /// payment validation mechanisms already known to the Connect API
    public Dictionary<string, object>? customSubscriberCredentialAuthKeys;

    /// Preferred DNS Server set here currently only apply to WireGuard VPN connections
    ///
    /// Default: (Cloudflare) 1.1.1.1, 1.0.0.1
    public string? preferredDNSServers;

    public static ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance) _logger = StaticLoggerFactory.CreateLogger("GRDVPNHelper");

            return _logger;
        }
    }

    public static GRDVPNHelper Singleton => _singleton ?? throw new InvalidOperationException();

    /// The GuardianConnect API hostname to use for the majority of API calls
    /// WARNING: Some API endpoints are always going to use the public Connect
    /// API hostname https://connect-api.guardianapp.com
    /// If no custom hostname is provided, the default public Connect API hostname is going to be used
    public string? ConnectAPIHostname { get; set; } = Common.DefaultConnectAPIHostname;

    /// GuardianConnect app key used to authenticate API requests
    public string? ConnectPublishableKey { get; } = null;

    /// don't set this value manually, it is set upon the region selection code working successfully
    public static string? PreferredRegion { get; set; }

    /// Precision <see cref="PreferredRegion"/> was chosen at. Empty means the
    /// historical default precision, so installations that predate the
    /// country/city selector keep working unchanged.
    public static string PreferredRegionPrecision { get; set; } = Common.kRegionPrecisionDefault;

    protected internal void SetForPrivate(bool preferBetaCapableServers,
        GRDServerFeatureEnvironment featureEnvironment)
    {
        _preferBetaCapableServers = preferBetaCapableServers;
        _featureEnvironment = featureEnvironment;
    }

    public static void CreateSingleton()
    {
        _logger = StaticLoggerFactory.CreateLogger<GRDVPNHelper>();
        _logger.LogInformation("GRDVPNHelper.CreateSingleton() - Entry.");
        _singleton = new GRDVPNHelper();
        _singleton._grdServerManager = new GRDServerManager();
        _singleton.PeToken = GRDPEToken.GetCurrentPEToken();

        _singleton.CurrentDeviceBlocklistConfig = new DeviceFilterConfig();

        GRDServerManager.InitialGeoInformationLoadComplete.Wait(1 * 1000);
        PreferredRegion = Preferences.Get(Common.kPreferredRegion, null!);
        var storedPrecision = Preferences.Get(Common.kPreferredRegionPrecision, null!);
        PreferredRegionPrecision = string.IsNullOrWhiteSpace(storedPrecision)
            ? Common.kRegionPrecisionDefault
            : storedPrecision;
    }

    /// Whether a RAS (IKEv2) connection is established, from the local RAS
    /// connection table. WireGuard tunnels are not RAS connections and are not
    /// seen here; <see cref="GetCurrentVPNState"/> covers both transports.
    public bool IsConnected(out string activeConnectionName)
    {
        activeConnectionName = string.Empty;
        _logger.LogInformation(
            "GRDVPNHelper.IsConnected: Calling Win32Calls.ConnectionRoutines.IsAnyConnectionActive()...");
        bool ifConnected;
        ifConnected = ConnectionRoutines.IsAnyConnectionActive(out var entryName);
        activeConnectionName = ConnectionRoutines.GetEntryNameOfActiveConnection();
        _logger.LogInformation(
            $"CheckConnectionState: IsConnected returned {ifConnected}. ACN='{activeConnectionName}',  Name='{entryName}'");

        return ifConnected;
    }

    public string GetNameOfConnectionEntry()
    {
        var isConnected = IsConnected(out var activeConnectionName);
        return isConnected ? activeConnectionName : string.Empty;
    }

    /// <summary>
    /// Returns true if a valid main credential matching <paramref name="protocol"/>
    /// is stored locally. Validity criteria are protocol-specific:
    /// <list type="bullet">
    /// <item>IKEv2: ApiAuthToken + UserName + Password + HostName all non-empty.</item>
    /// <item>WireGuard: DevicePrivateKey + DevicePublicKey + ServerPublicKey +
    ///       IPv4Address + ClientId + HostName all non-empty.</item>
    /// </list>
    /// The stored credential's <c>TransportProtocol</c> field must also match the
    /// requested protocol — a saved IKEv2 cred is not "active connection possible"
    /// for a WireGuard connect (and vice versa). Replaces the prior
    /// IKEv2-only implementation that silently returned false for any WG
    /// credential.
    /// </summary>
    public static bool ActiveConnectionPossible(GRDTransportProtocol.TransportProtocol? protocol = null)
    {
        // No protocol passed → use the user's current preferred protocol. (Optional
        // params can't default to a method call, so resolve here rather than in the
        // signature.) Callers may pass an explicit protocol to override.
        var p = protocol ?? GRDTransportProtocol.GetPreferred();

        var mainCreds = GRDCredentialManager.GetMainCredentials();
        if (mainCreds == null)
        {
            _logger.LogInformation("ActiveConnectionPossible({Protocol}): MainCredentials are not set", p);
            return false;
        }

        // No explicit mainCreds.TransportProtocol == protocol gate: the app
        // disconnect -> ClearVpnConfiguration -> SetPreferred -> reconnect
        // sequence on transport toggle guarantees stored creds match the
        // active protocol. And even in the absence of that guarantee, the
        // protocol-specific field validation below (UserName/Password/ApiAuthToken
        // for IKEv2; DevicePrivateKey/DevicePublicKey/etc. for WG) implicitly
        // catches a stale-other-protocol cred — the two field sets don't overlap.

        if (string.IsNullOrEmpty(mainCreds.HostName))
        {
            _logger.LogInformation("ActiveConnectionPossible({Protocol}): missing HostName", p);
            return false;
        }

        // Predicates pluck from the device-response DTO (disjoint field sets by
        // construction — the host only fills the negotiated protocol's subset,
        // so the IKEv2 predicate is false on a WG cred without any stuffing).
        // The WG device keypair stays on the flat fields: it's client-side, not
        // part of the host reply.
        mainCreds.EnsureDeviceFromLegacyFields();
        var device = mainCreds.Device!;
        bool valid = p switch
        {
            GRDTransportProtocol.TransportProtocol.TransportIKEv2 =>
                !string.IsNullOrEmpty(device.ApiAuthToken) &&
                !string.IsNullOrEmpty(device.EapUsername) &&
                !string.IsNullOrEmpty(device.EapPassword),
            GRDTransportProtocol.TransportProtocol.TransportWireGuard =>
                !string.IsNullOrEmpty(mainCreds.DevicePrivateKey) &&
                !string.IsNullOrEmpty(mainCreds.DevicePublicKey) &&
                !string.IsNullOrEmpty(device.ServerPublicKey) &&
                !string.IsNullOrEmpty(device.MappedIPv4Address) &&
                !string.IsNullOrEmpty(device.ClientId),
            _ => false,
        };

        // A credential that cannot carry the requested multi-hop exit is not reusable:
        // config/multihop is refused on a host that is not multihop-entry-enabled and
        // for an exit in the entry host's own city. Returning false routes the
        // connect through a fresh registration on a suitable entry host.
        var exit = ActiveMultihopExit();
        if (valid && exit is not null && !CanCarryMultihopExit(mainCreds, exit))
        {
            _logger.LogInformation(
                "ActiveConnectionPossible({Protocol}): stored host {Host} cannot carry multi-hop exit {Exit}; "
                + "a new registration is needed", p, mainCreds.HostName, exit);
            valid = false;
        }

        _logger.LogInformation(
            "ActiveConnectionPossible({Protocol}): result={Valid}", p, valid);
        return valid;
    }

    private static bool CanCarryMultihopExit(GRDCredential cred, string exit) =>
        cred.Server is { MultihopEntryEnabled: true } server
        && !string.Equals(server.RegionMultihopExitName, exit, StringComparison.OrdinalIgnoreCase);

    /// Used to clear all of our current VPN configuration details from user defaults and the keychain.
    /// Returns a Task (was async void) so consumers can await the server-side
    /// credential invalidate + local keychain wipe before flipping state that
    /// the invalidate depends on (e.g., the transport-protocol toggle's
    /// disconnect -> clear -> SetPreferred -> reconnect sequence).
    public async Task ClearVpnConfiguration()
    {
        ErrorResponse errorResponse;
        var mainCreds = GRDCredentialManager.GetMainCredentials();
        if (mainCreds != null )
        {
            // ClientId is populated symmetrically by GRDCredential.CreateFromDeviceResponse
            // for both protocols: IKEv2 copies the EAP user into ClientId; WG sets it from
            // the server's key-exchange response. No protocol-discriminated branch needed.
            var clientId = mainCreds.ClientId;
            (var subCreds, errorResponse) = await GetValidSubscriberCredentialWithCompletion();
            if (subCreds == null || errorResponse.Message.Equals(Common.kPETOKENNOTSET))
                return;

            errorResponse = await GRDGateway.InvalidateCredentialsForClientId(clientId, mainCreds.ApiAuthToken,
                mainCreds.HostName, subCreds.Jwt);
            if (errorResponse.IsError)
            {
                var responseMessage = errorResponse.Response as HttpResponseMessage;
                _logger.LogError(
                    $"Failed to invalidate VPN credentials: {responseMessage?.ReasonPhrase ?? errorResponse.Message})");
            }
            GRDCredentialManager.ClearMainCredentials();
        }
    }

    public void ClearAllGuardianRegistrySettings()
    {
        GRDKeychain.RemoveGuardianKeychainItems();
    }

    /// <summary>
    ///     Used as a helper to calling clients to return the name of the active connection, else null if not
    /// </summary>
    /// <returns>String of Connection Name</returns>
    public bool GetCurrentVPNState(out string connectionName)
    {
        _logger.LogInformation("In GetCurrentVPNState()");
        var state = ClientPipe.GetCurrentVpnConnectionStatus();
        _logger.LogInformation(
            $"GetCurrentVPNState: returned values for state are state: {state.ConnectionState}, entry: '{state.EntryName}'");
        var isConnected = state.ConnectionState == ConnectionStateEnum.Connected;
        connectionName = state.EntryName;
        return isConnected;
    }

    public async Task<ErrorResponse> ConnectVpnWithNewUserCredentialsForProtocol(
        GRDTransportProtocol.TransportProtocol protocol)
    {
        var errorResponse = new ErrorResponse();

        errorResponse = await CreateStandaloneCredentialsForTransportProtocol(protocol);
        if (errorResponse.IsError) return errorResponse;

        var credentials = (GRDCredential)errorResponse.Data!;

        var mainCredential = credentials;
        mainCredential.TransportProtocol = protocol;
        mainCredential.MainCredential = true;
        GRDCredentialManager.AddOrUpdateCredential(mainCredential);

        // Do connection call here
        errorResponse = await ConnectVPNTunnel();
        _logger.LogInformation(
            $"ConnectVpnWithNewUserCredentialsForProtocol: return from ConnectVPNTunnel - errorResponse.IsError == {errorResponse.IsError}");

        return errorResponse;
    }

    /// <summary>
    /// Overload that establishes credentials against an explicitly-chosen
    /// <paramref name="server"/> instead of the region auto-pick. The caller (e.g. the
    /// app's Developer window, when a host is double-clicked) supplies the
    /// <see cref="GRDSGWServer"/>; we create a fresh standalone credential for that
    /// server, persist it as the main credential, then connect.
    /// </summary>
    public async Task<ErrorResponse> ConnectVpnWithNewUserCredentialsForProtocol(
        GRDTransportProtocol.TransportProtocol protocol, GRDSGWServer? server)
    {
        if (server is null || string.IsNullOrWhiteSpace(server.Hostname))
            return new ErrorResponse()
                .SetException(new ArgumentException("server has no hostname", nameof(server)))
                .SetErrorMessage("No server supplied.");

        var errorResponse = await CreateStandaloneCredentialsForTransportProtocol(protocol, 30, server);
        if (errorResponse.IsError) return errorResponse;

        var mainCredential = (GRDCredential)errorResponse.Data!;
        mainCredential.TransportProtocol = protocol;
        mainCredential.MainCredential = true;
        mainCredential.HostName = server.Hostname;
        mainCredential.HostnameDisplayValue = server.HostLocation();
        // Captured while the record is in hand: a later stored-credential dial has
        // no host selection to repopulate the cache it came from.
        mainCredential.Server = server;
        GRDCredentialManager.AddOrUpdateCredential(mainCredential);

        errorResponse = await ConnectVPNTunnel();
        _logger.LogInformation(
            $"ConnectVpnWithNewUserCredentialsForProtocol(server): return from ConnectVPNTunnel - IsError == {errorResponse.IsError}");
        return errorResponse;
    }

    /// <summary>
    /// Connect entry point for a previously-configured user. Resolves to one of:
    /// <list type="bullet">
    /// <item>WG file-based: bring up tunnel directly from the wg-quick file
    ///       (the file IS the credential; no main-credential check needed).</item>
    /// <item>Stored creds for the current preferred protocol exist and the host
    ///       hasn't been overridden in the meantime → straight to the
    ///       protocol-specific "use stored creds" path
    ///       (<see cref="StartIKEv2Connection"/> or
    ///       <see cref="StartWireGuardFromStoredCreds"/>).</item>
    /// <item>No stored creds for this protocol →
    ///       go through the "exchange keys then start" path
    ///       (<see cref="ConnectVpnWithNewUserCredentialsForProtocol(GRDTransportProtocol.TransportProtocol)"/>
    ///       for both protocols).</item>
    /// </list>
    /// Replaces the prior asymmetric dispatch (IKEv2 had a credentials check +
    /// GetServerStatus pre-flight + host-override sync; WG had none of those).
    /// The credentials check (<see cref="ActiveConnectionPossible(GRDTransportProtocol.TransportProtocol?)"/>)
    /// is now protocol-aware and applied symmetrically.
    /// </summary>
    public async Task<ErrorResponse> ConnectVPNTunnel()
    {
        var protocol = GRDTransportProtocol.GetPreferred();

        // WG file-based shortcut: the wg-quick file IS the credential, so no
        // main-credential check / server-status pre-flight applies on this path.
        if (protocol == GRDTransportProtocol.TransportProtocol.TransportWireGuard
            && IsFileBasedWireGuardEnabled())
        {
            var wgConfigPath = RegistrySettings.RetrieveGuardianUserSettings(Common.kGuardianWireGuardConfigPath);
            if (string.IsNullOrWhiteSpace(wgConfigPath))
            {
                return new ErrorResponse()
                    .SetException(new InvalidOperationException(
                        "WireGuard is selected with file-based override but no config file path is configured."))
                    .SetErrorMessage("WireGuard config file is not set.");
            }
            return await StartWireGuardConnection(wgConfigPath);
        }

        // No valid stored creds for this protocol? Route to the single,
        // protocol-parameterized key-exchange path for BOTH protocols. It
        // establishes a fresh credential, persists it as the main credential,
        // then re-enters this method, which dials via the stored-creds path
        // below. (WireGuard previously had its own negotiate-and-dial method,
        // NegotiateAndStartWireGuard, that duplicated host-pick and tunnel
        // bring-up; removed in favor of this symmetric route.)
        if (!ActiveConnectionPossible(protocol))
        {
            if (protocol is not (GRDTransportProtocol.TransportProtocol.TransportIKEv2
                              or GRDTransportProtocol.TransportProtocol.TransportWireGuard))
            {
                return new ErrorResponse()
                    .SetException(new InvalidOperationException(
                        $"Unsupported transport protocol: {protocol}"))
                    .SetErrorMessage($"Unsupported transport protocol: {protocol}.");
            }
            return await ConnectVpnWithNewUserCredentialsForProtocol(protocol);
        }

        // Stored creds exist for this protocol. Pre-flight the host's
        // server-status endpoint before dialing — same call for both
        // protocols, using cred.HostName directly so both branches share
        // a single source of truth for the host (previously IKEv2 used
        // the ApiHostname static-property indirection while WG didn't
        // pre-flight at all).
        var cred = GRDCredentialManager.GetMainCredentials()!;

        // Stealth Mode: the gateway's published address replaces its hostname for
        // both the pre-flight below and the dial that follows, on either protocol.
        // The pre-flight is an HTTPS call, so leaving the hostname on it aborts the
        // connect before the dial is reached on a network that blocks resolution of
        // guardianapp.com.
        var stealthDialHost = await StealthDialAddressAsync(cred.HostName);
        var preflightHost = stealthDialHost ?? cred.HostName;

        var statusErr = await GRDGateway.GetServerStatus(preflightHost, clientCall: true);
        if (statusErr.IsError)
        {
            // When GetServerStatus throws (DNS failure, socket-block from KS,
            // connection refused, etc.) there's no HttpResponseMessage at all
            // and GetReasonPhrase() returns "OK" — the default reason phrase
            // on a fresh HttpResponseMessage. That produced the user-facing
            // lie "GetServerStatus returned: OK" while the actual cause was
            // a network failure. Surface the exception's message when present
            // so the error string reflects reality.
            var detail = statusErr.ThrownException is { } ex
                ? $"{ex.GetType().Name}: {ex.Message}"
                : $"HTTP {statusErr.GetReasonPhrase()}";
            return statusErr.SetErrorMessage(
                $"ConnectVPNTunnel: GetServerStatus failed: {detail}");
        }

        // The stored registration may carry a different exit than the preference
        // (changed while disconnected). ActiveConnectionPossible already guaranteed
        // the host can carry the wanted exit, so this is a config/multihop call.
        var exitErr = await ReconcileMultihopExitAsync(cred);
        if (exitErr.IsError) return exitErr;

        // Dial using stored creds.
        return protocol switch
        {
            GRDTransportProtocol.TransportProtocol.TransportIKEv2 =>
                await StartIKEv2Connection(stealthDialHost),
            GRDTransportProtocol.TransportProtocol.TransportWireGuard =>
                await StartWireGuardFromStoredCreds(stealthDialHost),
            _ => new ErrorResponse()
                .SetException(new InvalidOperationException(
                    $"Unsupported transport protocol: {protocol}"))
                .SetErrorMessage("Unsupported transport protocol."),
        };
    }

    /// <summary>
    /// True when HKCU\Software\GuardianFirewall\Settings\kGuardianUseFileBasedWireGuardConfig
    /// is "true" — i.e., the user has opted into supplying their own wg-quick file
    /// rather than letting the SDK establish one with the backend. Inverse default.
    /// </summary>
    private static bool IsFileBasedWireGuardEnabled() =>
        string.Equals(
            RegistrySettings.RetrieveGuardianUserSettings(Common.kGuardianUseFileBasedWireGuardConfig),
            "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the user has opted into the Smart Routing Proxy preference.
    /// </summary>
    public static bool IsSmartRoutingProxyEnabled() =>
        string.Equals(
            RegistrySettings.RetrieveGuardianUserSettings(Common.kGRDSmartRoutingProxyEnabled),
            "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Persists the user's Smart Routing Proxy preference to HKCU.
    /// </summary>
    public static void SetSmartRoutingProxyEnabled(bool enabled) =>
        RegistrySettings.UpdateGuardianUserSettings(
            Common.kGRDSmartRoutingProxyEnabled, enabled ? "true" : "false");

    /// <summary>
    /// True when the user has opted into Stealth Mode. This is the user preference
    /// alone; whether the gateway is actually dialed by address also depends on
    /// the host record carrying an IPv4 address, which is resolved at connect time.
    /// </summary>
    public static bool IsStealthModeEnabled() =>
        string.Equals(
            RegistrySettings.RetrieveGuardianUserSettings(Common.kGRDStealthModeEnabled),
            "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Persists the user's Stealth Mode preference to HKCU. Takes effect on the
    /// next connect, when the WireGuard config is built.
    /// </summary>
    public static void SetStealthModeEnabled(bool enabled) =>
        RegistrySettings.UpdateGuardianUserSettings(
            Common.kGRDStealthModeEnabled, enabled ? "true" : "false");

    /// <summary>True when the user has turned Multi-hop on.</summary>
    public static bool IsMultihopEnabled() =>
        string.Equals(
            RegistrySettings.RetrieveGuardianUserSettings(Common.kGRDMultihopEnabled),
            "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Persists the Multi-hop on/off preference to HKCU.</summary>
    public static void SetMultihopEnabled(bool enabled) =>
        RegistrySettings.UpdateGuardianUserSettings(
            Common.kGRDMultihopEnabled, enabled ? "true" : "false");

    /// <summary>The saved multi-hop exit slug, or null when none is chosen.</summary>
    public static string? GetMultihopExitRegion()
    {
        var exit = RegistrySettings.RetrieveGuardianUserSettings(Common.kGRDMultihopExitRegion);
        return string.IsNullOrWhiteSpace(exit) ? null : exit;
    }

    /// <summary>
    /// Persists the multi-hop exit slug (from <see cref="GRDServerManager.MultihopExitSlug"/>),
    /// or clears it with null.
    /// </summary>
    public static void SetMultihopExitRegion(string? exitSlug) =>
        RegistrySettings.UpdateGuardianUserSettings(Common.kGRDMultihopExitRegion, exitSlug ?? string.Empty);

    /// <summary>
    /// The exit to register or switch to: the saved slug when Multi-hop is on and an
    /// exit is chosen, otherwise null (single-hop).
    /// </summary>
    public static string? ActiveMultihopExit() => IsMultihopEnabled() ? GetMultihopExitRegion() : null;

    public enum MultihopChangeOutcome
    {
        /// Not connected; the preference is saved and applies on the next connect.
        Saved,
        /// The live connection now uses the new exit (or is back to single-hop).
        Applied,
        /// The current entry host cannot carry the new exit. The caller disconnects,
        /// clears the main credential and reconnects, which registers on a suitable host.
        ReconnectRequired,
        /// The gateway refused the change; the preference is restored to the exit
        /// still in effect.
        Failed,
    }

    /// <summary>
    /// Applies the saved Multi-hop preference to a live connection after the user
    /// changes it. An exit change, or turning Multi-hop off, is a config/multihop
    /// call that re-routes the existing tunnel. A new registration is needed only
    /// when the entry host cannot carry the exit: it is not multihop-entry-enabled,
    /// or the exit is its own city.
    /// </summary>
    public async Task<(MultihopChangeOutcome, ErrorResponse)> ApplyMultihopPreferenceAsync()
    {
        var cred = GRDCredentialManager.GetMainCredentials();
        if (cred is null || !GetCurrentVPNState(out _))
            return (MultihopChangeOutcome.Saved, new ErrorResponse());

        var exit = ActiveMultihopExit();
        if (exit is not null && !CanCarryMultihopExit(cred, exit))
        {
            _logger.LogInformation(
                "ApplyMultihopPreferenceAsync: {Host} cannot carry exit {Exit}; reconnect required",
                cred.HostName, exit);
            return (MultihopChangeOutcome.ReconnectRequired, new ErrorResponse());
        }

        var err = await ReconcileMultihopExitAsync(cred);
        if (!err.IsError) return (MultihopChangeOutcome.Applied, err);

        SetMultihopEnabled(!string.IsNullOrEmpty(cred.MultihopExitRegion));
        SetMultihopExitRegion(string.IsNullOrEmpty(cred.MultihopExitRegion) ? null : cred.MultihopExitRegion);
        return (MultihopChangeOutcome.Failed, err);
    }

    /// <summary>
    /// Brings the stored registration's exit in line with the preference with a
    /// config/multihop call, and records the result on the credential. No call is
    /// made when they already match.
    /// </summary>
    private async Task<ErrorResponse> ReconcileMultihopExitAsync(GRDCredential cred)
    {
        var wanted = ActiveMultihopExit() ?? string.Empty;
        if (string.Equals(wanted, cred.MultihopExitRegion, StringComparison.OrdinalIgnoreCase))
            return new ErrorResponse();

        var err = await GRDGateway.SetMultihopExitRegion(
            wanted.Length == 0 ? Common.kGRDMultihopDisabled : wanted);
        if (err.IsError) return err;

        cred.MultihopExitRegion = wanted;
        GRDCredentialManager.AddOrUpdateCredential(cred);
        return new ErrorResponse();
    }

    /// <summary>
    /// The published IPv4 address on the main credential's stored gateway record
    /// when Stealth Mode is on, or null when Stealth Mode is off or no address is
    /// stored. Reads local state only, so it needs no network.
    /// </summary>
    public static string? StoredStealthAddress()
    {
        if (!IsStealthModeEnabled()) return null;
        var stored = GRDCredentialManager.GetMainCredentials()?.Server?.IPv4Address;
        return string.IsNullOrWhiteSpace(stored) ? null : stored;
    }

    /// <summary>
    /// The gateway address to use in place of <paramref name="hostname"/> for this
    /// connection, or null to keep the hostname. Returns null when Stealth Mode is
    /// off, when no gateway record can be resolved, or when the record publishes no
    /// IPv4 address — in every one of those cases the hostname stands, so a missing
    /// address degrades to today's behavior rather than producing an unreachable
    /// endpoint.
    /// <para>
    /// The address stored on the credential is preferred because reading it needs
    /// no network at all. The record lookup behind it can fall back to an API call
    /// that resolves connect-api.guardianapp.com, which is exactly what a network
    /// hostile to Guardian blocks — so it serves credentials predating the stored
    /// field, not the case this feature is for.
    /// </para>
    /// </summary>
    private async Task<string?> StealthDialAddressAsync(string hostname)
    {
        if (!IsStealthModeEnabled()) return null;

        var stored = StoredStealthAddress();
        if (stored is not null)
        {
            _logger.LogInformation(
                "StealthDialAddressAsync: Stealth Mode on — using the address on the stored gateway "
                + "record, {Address}, for {Host}.", stored, hostname);
            return stored;
        }

        _logger.LogInformation(
            "StealthDialAddressAsync: no gateway record with an address stored on the credential for "
            + "{Host}; falling back to a host-record lookup, which needs name resolution.", hostname);

        var server = await GRDServerManager.FindHostRecordResilient(hostname);
        if (server is null)
        {
            _logger.LogWarning(
                "StealthDialAddressAsync: Stealth Mode is on but no gateway record resolved for "
                + "{Host}; connecting by hostname.", hostname);
            return null;
        }

        if (string.IsNullOrWhiteSpace(server.IPv4Address))
        {
            _logger.LogWarning(
                "StealthDialAddressAsync: Stealth Mode is on but {Host} publishes no IPv4 address; "
                + "connecting by hostname.", hostname);
            return null;
        }

        _logger.LogInformation(
            "StealthDialAddressAsync: Stealth Mode on — using {Address} in place of {Host}.",
            server.IPv4Address, hostname);
        return server.IPv4Address;
    }
    public async Task<ErrorResponse> DisconnectVPNTunnel()
    {
        var errorResponse = new ErrorResponse();
        _logger.LogInformation("In GRDVPNHelper.DisconnectVPNTunnel().");

        var entryName = GetNameOfConnectionEntry();
        _logger.LogInformation($"GRDVPNHelper.DisconnectVPNTunnel(): Name of entry to disconnect is '{entryName}'");

        _logger.LogInformation(
            $"GRDVPNHelper.DisconnectVPNTunnel(): Calling ClientPipe.DisconnectVPNConnectionAsync() to disconnect '{entryName}'");
        try
        {
            await Task.Run(() =>
            {
                _logger.LogInformation("GRDVPNHelper.DisconnectVPNTunnel(): Inside Task.Run()");
                ClientPipe.DisconnectVPNConnection(entryName);
                errorResponse.Message = "Disconnected successfully";
            });
        }
        catch (Exception e)
        {
            _logger.LogError(e,
                $"GRDVPNHelper.DisconnectVPNTunnel(): Exception during ClientPipe.DisconnectVPNConnectionAsync() for entry '{entryName}'");
            errorResponse.SetException(e);
            if (e.InnerException != null && e.InnerException is IOException)
                errorResponse.SetErrorMessage("PIPE BROKEN. COMMUNICAION TO SERVICE LOST.");
            else
                errorResponse.SetErrorMessage(e.Message);
        }

        _logger.LogInformation("GRDVPNHelper.DisconnectVPNTunnel(): Back from ClientPipe.DisconnectVPNConnectionAsync()");

        return errorResponse;
    }

    /// There should be no need to call this directly, this is for internal use only.
    public async Task<(GRDSubscriberCredential?, ErrorResponse)> GetValidSubscriberCredentialWithCompletion()
    {

        ErrorResponse errorResponse;
        var subCred = GRDSubscriberCredential.GetCurrentStoredSubscriberCredential();
        if (!subCred.IsEmpty && !subCred.IsTokenExpired)
        {
            GRDHousekeepingAPI.LiveGrdCredential = subCred;
            return (GRDHousekeepingAPI.LiveGrdCredential, new ErrorResponse(string.Empty));
        }

        var peToken = GRDKeychain.GetPasswordStringForAccount(Common.kKeychainStr_PEToken_Itself);
        if (string.IsNullOrEmpty(peToken))
        {
            errorResponse = new ErrorResponse(Common.kPETOKENNOTSET, null, true);
            return (null, errorResponse);
        }

        errorResponse = await GRDHousekeepingAPI.CreateSubscriberCredentialForBundleId(peToken);
        return (GRDHousekeepingAPI.LiveGrdCredential, errorResponse);
    }

    public async Task<ErrorResponse> CreateStandaloneCredentialsForTransportProtocol(
        GRDTransportProtocol.TransportProtocol protocol, int validForDays = 30)
    {
        var errorResponse = new ErrorResponse();

        GRDSGWServer selectedServer;

        var multihopExit = ActiveMultihopExit();
        var (server, hostErr) =
            GRDServerManager.SelectGuardianHostWithCompletion(PreferredRegion, PreferredRegionPrecision, multihopExit);
        if (hostErr.IsError)
        {
            _logger.LogError(
                "CreateStandaloneCredentialsForTransportProtocol: host selection failed: {Msg}", hostErr.Message);
            return hostErr;
        }

        selectedServer = server;

        // PROTOPICK
        errorResponse = await CreateStandaloneCredentialsForTransportProtocol(
            protocol, validForDays, selectedServer, multihopExit);
        if (errorResponse.IsError) return errorResponse;

        // adding in host info here instead of above in caller
        var credentials = (GRDCredential)errorResponse.Data!;
        credentials.HostName = selectedServer.Hostname;
        credentials.HostnameDisplayValue = selectedServer.HostLocation();
        credentials.Server = selectedServer;
        return new ErrorResponse().SetData(credentials);
    }

    /// Used to create standalone VPN credentials on a specified host that is valid for a certain number of days. Good for exporting VPN
    /// credentials for use on other devices.
    /// @param protocol The desired transport protocol to use to establish the connection. IKEv2 (builtin) as well as WireGuard via a
    /// PacketTunnelProvider are supported
    /// @param days number of days these credentials will be valid for
    /// @param server the GRDSGWServer (GRDSGWServer) to create credentials for
    /// @param completion block Completion block that will contain an NSDictionary of credentials upon success
    /// @param multihopExitRegion exit slug to register with, or null for single-hop
    public async Task<ErrorResponse> CreateStandaloneCredentialsForTransportProtocol(
        GRDTransportProtocol.TransportProtocol protocol, int days, GRDSGWServer server,
        string? multihopExitRegion = null)
    {
        ErrorResponse errorResponse;
        (var subCreds, errorResponse) = await GetValidSubscriberCredentialWithCompletion();
        if (errorResponse.IsError) return errorResponse;
        if (subCreds is null)
        {
            errorResponse = new ErrorResponse("SubscriberCredentials is null!", null, true);
            return errorResponse;
        }
        errorResponse = await GRDGateway.RegisterDeviceForTransportProtocol(
            protocol, server.Hostname, subCreds.Jwt, days, multihopExitRegion);

        return errorResponse;
    }

    /// Verify that the current main VPN credentials are valid if applicable. A valid Subscriber Credential is automatically obtained and provided
    /// to the VPN node alongside
    /// the credential details. If the device is currently connected and the server indicates that the VPN credentials are no longer valid the
    /// device is automatically
    /// migrated to a new server within the same region
    public void VerifyMainCredentialsWithCompletion(Action<bool, string> completion)
    {
    }

    /// Call this to properly assign a GRDRegion to all GRDServerManager instances
    /// @param region the region to select a server from. Pass nil to reset to Automatic region selection mode
    public void SetPreferredRegion(string? regionNameKey)
    {
        PreferredRegion = regionNameKey;
        PreferredRegionPrecision = Common.kRegionPrecisionDefault;
    }

    /// Assign a preferred region chosen at a specific precision. A city key must
    /// be paired with kRegionPrecisionCity and a country key with
    /// kRegionPrecisionCountry, because the same name can exist at more than one
    /// precision. Pass null to reset to Automatic region selection mode.
    public void SetPreferredRegion(string? regionNameKey, string regionPrecision)
    {
        PreferredRegion = regionNameKey;
        PreferredRegionPrecision = string.IsNullOrWhiteSpace(regionPrecision)
            ? Common.kRegionPrecisionDefault
            : regionPrecision;
    }

    /// <param name="sgwServerAddressOverride">
    /// When set, dialed as the RAS gateway in place of the credential's hostname.
    /// Stealth Mode supplies the gateway's published IPv4 address. Windows validates
    /// the IKE certificate against the address it dialed, which the gateway
    /// certificate covers with an iPAddress SAN.
    /// </param>
    private async Task<ErrorResponse> StartIKEv2Connection(string? sgwServerAddressOverride = null)
    {
        var errorResponse = new ErrorResponse();

        var mainCredential = GRDCredentialManager.GetMainCredentials();
        if (mainCredential is null)
        {
            return errorResponse
                .SetException(new InvalidOperationException(
                    "StartIKEv2Connection called without a stored MainCredential."))
                .SetErrorMessage("No stored VPN credential.");
        }

        // Pluck EAP creds from the device-response DTO (EnsureDevice backfills
        // it for legacy creds). Host stays on the flat field — it's the chosen
        // host, not part of the device reply.
        mainCredential.EnsureDeviceFromLegacyFields();
        var device = mainCredential.Device!;
        // Make IPC call to GuardianWindowsService to start the connection
        var vpnValues = new VPNCallParameters
        {
            Transport = GRDTransportProtocol.TransportProtocol.TransportIKEv2,
            VpnHostName = string.IsNullOrWhiteSpace(sgwServerAddressOverride)
                ? mainCredential.HostName
                : sgwServerAddressOverride!,
            VpnHostDisplay = mainCredential.HostnameDisplayValue,
            EapuserName = device.EapUsername ?? string.Empty,
            Eappassword = device.EapPassword ?? string.Empty,
            EntryName = $"Guardian Firewall - {mainCredential.HostnameDisplayValue}"
        };

        _logger.LogInformation("StartIKEv2Connection: Starting VPN connection...");

        try
        {
            _logger.LogInformation("StartIKEv2Connection: Calling ClientPipe.StartVPNConnection[12120934]...");
            errorResponse = await ClientPipe.StartVPNConnection(vpnValues);
            _logger.LogInformation("StartIKEv2Connection: Past call to ClientPipe.StartVPNConnection[12120934]");
            if (errorResponse.IsError)
                _logger.LogError(
                    $"StartIKEv2Connection: FAILURE to establish VPN connection. ErrorResponse = {errorResponse}");
            else
                _logger.LogInformation("StartIKEv2Connection: VPN connection established.");
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            errorResponse.SetException(e).SetErrorMessage(e.Message);
            _logger.LogError(e, $"{errorResponse}");
        }

        _logger.LogInformation(
            $"StartIKEv2Connection: returning with errorResponse.IsError == {errorResponse.IsError}");
        return errorResponse;
    }

    /// <summary>
    /// Bring up the WG tunnel from an already-persisted main credential. Parallels
    /// <see cref="StartIKEv2Connection"/> for the IKEv2 path. Selected by the
    /// dispatcher in <see cref="ConnectVPNTunnel"/> when
    /// <see cref="ActiveConnectionPossible(GRDTransportProtocol.TransportProtocol?)"/>
    /// returns true for WireGuard — i.e., we have a valid cached cred and don't
    /// need to exchange keys.
    /// </summary>
    /// <param name="sgwServerAddressOverride">
    /// When set, used as the WireGuard Endpoint host in place of the credential's
    /// hostname. Stealth Mode supplies the gateway's published IPv4 address.
    /// </param>
    private async Task<ErrorResponse> StartWireGuardFromStoredCreds(string? sgwServerAddressOverride = null)
    {
        var errorResponse = new ErrorResponse();
        _logger.LogInformation("StartWireGuardFromStoredCreds: entry");

        var cred = GRDCredentialManager.GetMainCredentials();
        if (cred is null
            || cred.TransportProtocol != GRDTransportProtocol.TransportProtocol.TransportWireGuard)
        {
            return errorResponse
                .SetException(new InvalidOperationException(
                    "StartWireGuardFromStoredCreds called without a WireGuard MainCredential."))
                .SetErrorMessage("No stored WireGuard credential.");
        }

        var dnsSRPMode = IsSmartRoutingProxyEnabled();
        GRDSGWServer? srpServer = null;
        if (dnsSRPMode)
        {
            srpServer = await GRDServerManager.FindHostRecordResilient(cred.HostName);
            if (srpServer is null)
                _logger.LogWarning(
                    "StartWireGuardFromStoredCreds: could not resolve a gateway record for {Host}; "
                    + "Smart Routing Proxy stays off for this connection.", cred.HostName);
        }

        var configText = GRDWireGuardConfiguration.WireGuardQuickConfigForCredential(
            cred, null, srpServer, dnsSRPMode, sgwServerAddressOverride);
        if (string.IsNullOrEmpty(configText))
        {
            return errorResponse
                .SetException(new InvalidOperationException(
                    "WireGuardQuickConfigForCredential returned null — stored credential is incomplete."))
                .SetErrorMessage("Failed to build WireGuard config from stored credential.");
        }

        var vpnValues = new VPNCallParameters
        {
            Transport            = GRDTransportProtocol.TransportProtocol.TransportWireGuard,
            EntryName            = $"Guardian WireGuard - {cred.HostnameDisplayValue}",
            WireGuardConfigText  = configText,
            VpnHostName          = string.IsNullOrWhiteSpace(sgwServerAddressOverride)
                ? cred.HostName
                : sgwServerAddressOverride!,
            VpnHostDisplay       = cred.HostnameDisplayValue,
        };

        try
        {
            errorResponse = await ClientPipe.StartVPNConnection(vpnValues);
            if (errorResponse.IsError)
                _logger.LogError(
                    "StartWireGuardFromStoredCreds: service refused start: {Msg}",
                    errorResponse.Message);
            else
                _logger.LogInformation(
                    "StartWireGuardFromStoredCreds: tunnel up on host {Host}", cred.HostName);
        }
        catch (Exception e)
        {
            errorResponse.SetException(e).SetErrorMessage(e.Message);
            _logger.LogError(e, "StartWireGuardFromStoredCreds: ClientPipe threw");
        }

        return errorResponse;
    }

    private async Task<ErrorResponse> StartWireGuardConnection(string configPath)
    {
        var errorResponse = new ErrorResponse();
        _logger.LogInformation($"StartWireGuardConnection: configPath='{configPath}'");

        // EAP/IKEv2 fields stay empty — they're irrelevant on this code path.
        var vpnValues = new VPNCallParameters
        {
            Transport = GRDTransportProtocol.TransportProtocol.TransportWireGuard,
            EntryName = "Guardian FirewallWireGuard",
            WireGuardConfigPath = configPath,
        };

        try
        {
            errorResponse = await ClientPipe.StartVPNConnection(vpnValues);
            if (errorResponse.IsError)
                _logger.LogError(
                    $"StartWireGuardConnection: FAILURE to establish VPN connection. ErrorResponse = {errorResponse}");
            else
                _logger.LogInformation("StartWireGuardConnection: VPN connection established.");
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            errorResponse.SetException(e).SetErrorMessage(e.Message);
            _logger.LogError(e, $"{errorResponse}");
        }

        return errorResponse;
    }

    /// Clear all on device cache related to cached Guardian hosts & keychain items including the Subscriber Credential
    public void ClearLocalCache()
    {
        GRDKeychain.RemoveGuardianKeychainItems();
        GRDKeychain.RemoveSubscriberCredentialWithRetries(3);
    }
}