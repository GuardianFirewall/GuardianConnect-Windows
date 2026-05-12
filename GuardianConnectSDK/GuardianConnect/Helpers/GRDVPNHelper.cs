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
    }

    /// Helper function to quickly determine if a VPN tunnel of any kind
    /// with any transport protocol is established
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

    /// Used to determine if an active connection is possible, do we have all the necessary credentials (EAPUsername, Password, Host, etc)
    public static bool ActiveConnectionPossible()
    {
        _logger.LogInformation("In ActiveConnectionPossible()");
        var mainCreds = GRDCredentialManager.GetMainCredentials();
        if (mainCreds == null)
        {
            _logger.LogInformation("ActiveConnectionPossible(): MainCredentials are not set");
            return false;
        }

        if (mainCreds.TransportProtocol == ITransportProvider.TransportProtocol.TransportIKEv2
            && !string.IsNullOrEmpty(mainCreds.HostName)
            && !string.IsNullOrEmpty(mainCreds.ApiAuthToken)
            && !string.IsNullOrEmpty(mainCreds.UserName))
        {
            _logger.LogInformation("ActiveConnectionPossible(): MainCredentials are valid");
            return true;
        }

        _logger.LogInformation("ActiveConnectionPossible(): MainCredentials are not valid");
        return false;
    }

    /// <summary>
    ///     Used to wipe out portion of MainCredentials to cause re-obtain when a new region is selected
    /// </summary>
    public static void ResetMainCredentials()
    {
        _logger.LogInformation("In ResetMainCredentials()");
        GRDCredentialManager.ClearMainCredentials();
    }

    /// Used to clear all of our current VPN configuration details from user defaults and the keychain
    public async void ClearVpnConfiguration()
    {
        ErrorResponse errorResponse;
        var mainCreds = GRDCredentialManager.GetMainCredentials();
        if (mainCreds != null
//                &&
//                !string.IsNullOrEmpty(mainCreds.ClientId)
           )
        {
            var clientId = string.Empty;
            if (mainCreds.TransportProtocol == ITransportProvider.TransportProtocol.TransportIKEv2)
                clientId = mainCreds.UserName;
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
        }
    }

    public void ClearAllGuardianRegistrySettings()
    {
        GRDCredentialManager.ClearMainCredentials();
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

    public void ConfigureFirstTimeUserPostCredential(Action mid, Action<bool, string> completion)
    {
        var (host, hostLocation, errorResponse) =
            GRDServerManager.SelectGuardianHostWithCompletion(PreferredRegion);
    }

    public async Task<ErrorResponse> ConnectVpnWithNewUserCredentialsForProtocol(
        ITransportProvider.TransportProtocol protocol)
    {
        var errorResponse = new ErrorResponse();

        errorResponse = await CreateStandaloneCredentialsForTransportProtocol(protocol);
        if (errorResponse.IsError) return errorResponse;

        var credentials = (List<GRDCredential>)errorResponse.Data!;

        var mainCredential = credentials[0];
        mainCredential.TransportProtocol = protocol;
        mainCredential.MainCredential = true;
        GRDCredentialManager.AddOrUpdateCredential(mainCredential);

        // Do connection call here
        errorResponse = await ConnectVpnWithConfiguredCredentials();
        _logger.LogInformation(
            $"ConnectVpnWithNewUserCredentialsForProtocol: return from ConnectVpnWithConfiguredCredentials - errorResponse.IsError == {errorResponse.IsError}");

        return errorResponse;
    }

    /// Used subsequently after the first time connection has been successfully made to re-connect to the current host VPN node with mainCredentials
    /// @param completion block This completion block will return a message to display to the user and a status code, if the connection is successful,
    /// the message will be empty.
    public async Task<ErrorResponse> ConnectVpnWithConfiguredCredentials()
    {
        ErrorResponse errorResponse;

        // Transport selection comes from the user's saved preference, not the
        // credential — WireGuard configs come from a file on disk (and later
        // from CJ's backend), separate from the IKEv2 EAP credentials flow.
        var selectedTransport = RegistrySettings.RetrieveGuardianUserSettings(Common.kGuardianTransportProtocol);
        if (string.Equals(selectedTransport, "WireGuard", StringComparison.OrdinalIgnoreCase))
        {
            var wgConfigPath = RegistrySettings.RetrieveGuardianUserSettings(Common.kGuardianWireGuardConfigPath);
            if (string.IsNullOrWhiteSpace(wgConfigPath))
            {
                return new ErrorResponse()
                    .SetException(new InvalidOperationException(
                        "WireGuard is selected but no config file path is configured."))
                    .SetErrorMessage("WireGuard config file is not set.");
            }
            return await StartWireGuardConnection(wgConfigPath);
        }

        // Need to check if we've set our local copy of credentials and if null then grab from GRDCM
        var mainCredentials = GRDCredentialManager.GetMainCredentials();
        if (mainCredentials == null
            || string.IsNullOrEmpty(mainCredentials.HostName)
            || string.IsNullOrEmpty(mainCredentials.ApiAuthToken))
            _logger.LogInformation("GRDVPNHelper: main credentials not set. Syncing now.");

        errorResponse = await GRDGateway.GetServerStatus();
        if (errorResponse.IsError)
        {
            errorResponse.SetErrorMessage(
                $"ConnectVpnWithConfiguredCredentials: GetServerStatus returned: {errorResponse.GetReasonPhrase()}");
            return errorResponse;
        }

        if (mainCredentials!.TransportProtocol != ITransportProvider.TransportProtocol.TransportIKEv2)
        {
            errorResponse.SetException(new InvalidOperationException("MainCredential.TransportProtocol not set!"))
                .SetErrorMessage("WHY CALLING StartIKEv2Connection WITH PROTOCOL NOT SET??");
            return errorResponse;
        }

        var apiAuthToken = mainCredentials.ApiAuthToken;
        var eapUsername = mainCredentials.UserName;
        var eapPassword = mainCredentials.Password;
        if (!string.IsNullOrEmpty(apiAuthToken) && !string.IsNullOrEmpty(eapUsername) &&
            !string.IsNullOrEmpty(eapPassword))
        {
            // Credentials are usable - let's continue
            errorResponse = await StartIKEv2Connection();
            _logger.LogInformation(
                $"ConnectVpnWithConfiguredCredentials: return from StartIKEv2Connection - errorResponse.IsError == {errorResponse.IsError}");


            return errorResponse;
        }

        // Return error that credentials are bad
        errorResponse.SetException(new Exception("Credentials are not set! VPN Connection not made"));

        return errorResponse;
    }

    public async Task<ErrorResponse> DisconnectVPN()
    {
        var errorResponse = new ErrorResponse();
        _logger.LogInformation("In GRDVPNHelper.DisconnectVPN().");

        var entryName = GetNameOfConnectionEntry();
        _logger.LogInformation($"GRDVPNHelper.DisconnectVPN(): Name of entry to disconnect is '{entryName}'");

        _logger.LogInformation(
            $"GRDVPNHelper.DisconnectVPN(): Calling ClientPipe.DisconnectVPNConnectionAsync() to disconnect '{entryName}'");
        try
        {
            await Task.Run(() =>
            {
                _logger.LogInformation("GRDVPNHelper.DisconnectVPN(): Inside Task.Run()");
                ClientPipe.DisconnectVPNConnection(entryName);
                errorResponse.Message = "Disconnected successfully";
            });
        }
        catch (Exception e)
        {
            _logger.LogError(e,
                $"GRDVPNHelper.DisconnectVPN(): Exception during ClientPipe.DisconnectVPNConnectionAsync() for entry '{entryName}'");
            errorResponse.SetException(e);
            if (e.InnerException != null && e.InnerException is IOException)
                errorResponse.SetErrorMessage("PIPE BROKEN. COMMUNICAION TO SERVICE LOST.");
            else
                errorResponse.SetErrorMessage(e.Message);
        }

        _logger.LogInformation("GRDVPNHelper.DisconnectVPN(): Back from ClientPipe.DisconnectVPNConnectionAsync()");

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
        ITransportProvider.TransportProtocol protocol, int validForDays = 30)
    {
        var errorResponse = new ErrorResponse();

        var (host, hostDisplay, error) = GRDServerManager.SelectGuardianHostWithCompletion(PreferredRegion);
        errorResponse = await CreateStandaloneCredentialsForTransportProtocol(protocol, validForDays, host);
        if (errorResponse.IsError) return errorResponse;

        // adding in host info here instead of above in caller
        var credentials = (List<GRDCredential>)errorResponse.Data!;
        credentials[0].HostName = host;
        credentials[0].HostnameDisplayValue = hostDisplay;
        return new ErrorResponse().SetData(credentials);
    }

    /// Used to create standalone VPN credentials on a specified host that is valid for a certain number of days. Good for exporting VPN
    /// credentials for use on other devices.
    /// @param protocol The desired transport protocol to use to establish the connection. IKEv2 (builtin) as well as WireGuard via a
    /// PacketTunnelProvider are supported
    /// @param days NSInteger number of days these credentials will be valid for
    /// @param hostname NSString hostname to connect to ie: saopaulo-ipsec-4.sudosecuritygroup.com
    /// @param completion block Completion block that will contain an NSDictionary of credentials upon success
    public async Task<ErrorResponse> CreateStandaloneCredentialsForTransportProtocol(
        ITransportProvider.TransportProtocol protocol, int days, string hostname)
    {
        ErrorResponse errorResponse;
        (var subCreds, errorResponse) = await GetValidSubscriberCredentialWithCompletion();
        if (errorResponse.IsError) return errorResponse;
        errorResponse = await GRDGateway.RegisterDeviceForTransportProtocol(protocol, hostname, subCreds!.Jwt, days);

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
    }

    private async Task<ErrorResponse> StartIKEv2Connection()
    {
        var errorResponse = new ErrorResponse();

        var mainCredential = GRDCredentialManager.GetMainCredentials();
        // Make WCF call to GuardianWindowsService to start the connection
        var vpnValues = new VPNCallParameters
        {
            VpnHostName = mainCredential!.HostName,
            VpnHostDisplay = mainCredential.HostnameDisplayValue,
            EapuserName = mainCredential.UserName,
            Eappassword = mainCredential.Password,
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

    private async Task<ErrorResponse> StartWireGuardConnection(string configPath)
    {
        var errorResponse = new ErrorResponse();
        _logger.LogInformation($"StartWireGuardConnection: configPath='{configPath}'");

        // Dispatcher selects the WireGuard transport implicitly when the request
        // carries a WireGuardConfigPath. EAP/IKEv2 fields stay empty — they're
        // irrelevant on this code path.
        var vpnValues = new VPNCallParameters
        {
            EntryName = "Guardian WireGuard",
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