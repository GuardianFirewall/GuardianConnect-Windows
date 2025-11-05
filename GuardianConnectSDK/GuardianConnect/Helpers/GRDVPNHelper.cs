using System.Diagnostics;
using System.Runtime.CompilerServices;
using GuardianConnect.Abstractions;
using GuardianConnect.API;
using GuardianConnect.API.Model;
using GuardianConnect.Credentials;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using GuardianConnect.VPNTransports;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("GuardianCore")]

namespace GuardianConnect.Helpers
{
     public class GRDVPNHelper
     {
         private static bool _instanceCreated = false;
         private static Microsoft.Extensions.Logging.ILogger _logger;

         public enum GRDVPNHelperStatusCode {
             GRDVPNHelperSuccess,
             GRDVPNHelperFail,
             GRDVPNHelperDoesNeedMigration,
             GRDVPNHelperMigrating,
             GRDVPNHelperNetworkConnectionError, // add other network errors
             GRDVPNHelperCoudNotReachAPIError,
             GRDVPNHelperApp_VpnPrefsLoadError,
             GRDVPNHelperApp_VpnPrefsSaveError,
             GRDVPNHelperAPI_AuthenticationError,
             GRDVPNHelperAPI_ProvisioningError
         }
             
         protected internal bool _preferBetaCapableServers;
         protected internal GRDServerFeatureEnvironment? _featureEnvironment;
         public GRDPEToken? PeToken;
         public static bool IsClientSet = false;
        public static Microsoft.Extensions.Logging.ILogger Logger
         {
             get => _logger;
             set => _logger = value;
        }

        public DeviceFilterConfig? CurrentDeviceBlocklistConfig;
         
         private GRDServerManager? _grdServerManager;

         protected internal void SetForPrivate(bool preferBetaCapableServers, GRDServerFeatureEnvironment featureEnvironment)
         {
             _preferBetaCapableServers = preferBetaCapableServers;
             _featureEnvironment = featureEnvironment;
         }
         
         protected internal GRDVPNHelper(bool preferBetaCapableServers, GRDServerFeatureEnvironment featureEnvironment)
         {
             _preferBetaCapableServers = preferBetaCapableServers;
             _featureEnvironment = featureEnvironment;
//             if (!_instanceCreated)
//             {
//                 CreateInstance(preferBetaCapableServers, featureEnvironment);
//             }
         }
         
         // Set up a singleton
         private static GRDVPNHelper? _instance;
         
         // TJE - TODO - Remove and refactor
         /// a read only reference to the global NEVPNManager which handles
         /// IKEv2 connections. This should be used as a read-only reference to convenient access
         /// <param name="port"></param>

         public static GRDVPNHelper Instance
         {
             get
             {
                 if (_instance == null)
                 {
                     // WHAT??? Who's calling the get before we call the CreateInstance()?
                     Debugger.Break();
                 }

                 return _instance ?? throw new InvalidOperationException();
             }
         }

         public static void CreateInstance(bool prefBetaServers, GRDServerFeatureEnvironment featureEnv)
         {
             _logger = StaticLoggerFactory.CreateLogger<GRDVPNHelper>();
             _logger.LogInformation("GRDVPNHelper.CreateInstance() - Entry.");
             _instanceCreated = true;
             _instance = new GRDVPNHelper(prefBetaServers, featureEnv);
             _instance._grdServerManager = new GRDServerManager();
             _instance.mainCredential = GRDCredentialManager.MainCredentials;
             _instance.PeToken = GRDPEToken.GetCurrentPEToken();

             _instance.CurrentDeviceBlocklistConfig = new DeviceFilterConfig()
             {
                 Api_auth_token = GRDCredentialManager.MainCredentials == null ? "" :
                     GRDCredentialManager.MainCredentials.ApiAuthToken == null ? "" :
                     GRDCredentialManager.MainCredentials.ApiAuthToken,
             };
         }

         public enum GRDServerFeatureEnvironment
         {
             ServerFeatureEnvironmentProduction = 1,
             ServerFeatureEnvironmentInternal,
             ServerFeatureEnvironmentDevelopment,
             ServerFeatureEnvironmentDualStack,
             ServerFeatureEnvironmentUnstable  
         }

         public readonly bool PreferBetaCapableServers;
         public readonly GRDServerFeatureEnvironment FeatureEnvironment;
         
        /// The GuardianConnect API hostname to use for the majority of API calls
        /// WARNING: Some API endpoints are always going to use the public Connect
        /// API hostname https://connect-api.guardianapp.com
        /// If no custom hostname is provided, the default public Connect API hostname is going to be used
        public string? ConnectAPIHostname { get; set; }

        /// GuardianConnect app key used to authenticate API requests
        public string? ConnectPublishableKey { get; }

        /// can be set to true to make - (void)getEvents return dummy alerts for debugging purposes
	    public bool DummyDataForDebugging { get; }

        /// don't set this value manually, it is set upon the region selection code working successfully
        public GRDRegion? SelectedRegion { get; }

        /// a separate reference is kept of the mainCredential because the credential manager instance needs to be fetched from preferences
        /// & the keychain every time its called.
        private GRDCredential? _mainCredential;
        public  GRDCredential? mainCredential
        {
            get => _mainCredential;
            set
            {
                StackTrace stackTrace = new StackTrace();           // get call stack
                StackFrame[] stackFrames = stackTrace.GetFrames();  // get method calls (frames)

                var sfm2 =
                    $"{stackFrames[^2].GetMethod()}:{stackFrames[^2].GetFileLineNumber():dd}";
                var sfm1 =
                    $"{stackFrames[^1].GetMethod()}:{stackFrames[^1].GetFileLineNumber():dd}";
                _logger.LogInformation($"Callers to setting mainCredential...");
                _logger.LogDebug($"{sfm2}");
                _logger.LogDebug($"{sfm1}");
                _mainCredential = value ?? null;
            }
        }

        /// This string will be used as the localized description of the NEVPNManager
        /// configuration. The string will be visible in the network preferences on macOS
        /// or in the VPN settings on iOS/iPadOS
        ///
        /// Please note that this value is different than the grdTunnelProviderManagerLocalizedDescription
        /// and it is not recommended to set the same values for both tunnels to avoid customers confusion
        public string? TunnelLocalizedDescription { get; set; }


        /// Indicate whether or not GRDVPNHelper should append a formatted server
        /// location string at the end of the localized tunnel description string
        ///
        /// Eg. "Guardian Firewall" -> "Guardian Firewall: Frankfurt, Germany"
        public bool AppendServerRegionToTunnelLocalizedDescription { get; set; }

        /// Tunnel provider manager wrapper class to help with
        /// starting and stopping a WireGuard VPN tunnel or a local tunnel.
        /// private static GRDTunnelManager tunnelManager;

        /// Bundle Identifier string of the PacketTunnelProvider bundled with the main app.
        /// May be omitted if WireGuard as the Transport Protocol or a local tunnel is not used.
        /// It is recommended to set this up as early as possible
        public string? tunnelProviderBundleIdentifier;

        /// Preferred DNS Server set here currently only apply to WireGuard VPN connections
        ///
        /// Default: (Cloudflare) 1.1.1.1, 1.0.0.1
        public string? preferredDNSServers;

        /// Set this key/value combinations to authenticate for custom
        /// payment validation mechanisms already known to the Connect API
        public Dictionary<string, object>? customSubscriberCredentialAuthKeys;

        /// Always use the sharedInstance of this class, call it as early as possible in your application lifecycle to initialize
        /// the VPN preferences and load the credentials and VPN node connection information from the keychain.
        public static GRDVPNHelper sharedInstance => _instance ?? throw new InvalidOperationException();

        /// Helper function to quickly determine if a VPN tunnel of any kind
        /// with any transport protocol is established
        public bool IsConnected(out string activeConnectionName)
        {
            activeConnectionName = string.Empty;
            _logger.LogInformation( "GRDVPNHelper.IsConnected: Calling Win32Calls.ConnectionRoutines.IsAnyConnectionActive()...");
            bool ifConnected;
            ifConnected = Win32Calls.ConnectionRoutines.IsAnyConnectionActive(out string entryName);
            activeConnectionName = Win32Calls.ConnectionRoutines.GetEntryNameOfActiveConnection();
            _logger.LogInformation($"CheckConnectionState: IsConnected returned {ifConnected}. ACN='{activeConnectionName}',  Name='{entryName}'");

            return ifConnected;
        }

        public String GetNameOfConnectionEntry()
        {
            var isConnected = IsConnected(out string activeConnectionName);
            return isConnected ? activeConnectionName : string.Empty;
        }

        public bool IsBusy;


        /// retrieves values out of the system keychain and stores them in the sharedInstance singleton object in memory for other
        /// functions to use in the future
        public void _loadCredentialsFromKeychain()
        {
            mainCredential = GRDCredentialManager.MainCredentials;
        }

        /// Used to determine if an active connection is possible, do we have all the necessary credentials (EAPUsername, Password, Host, etc)
        public static bool ActiveConnectionPossible()
        {
            _logger.LogInformation("In ActiveConnectionPossible()");
            if (GRDCredentialManager.MainCredentials == null)
            {
                _logger.LogInformation("ActiveConnectionPossible(): MainCredentials are not set");
                return false;
            }
            
            GRDCredential mainCreds = GRDCredentialManager.MainCredentials;
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
        /// Used to wipe out portion of MainCredentials to cause re-obtain when a new region is selected
        /// </summary>
        public static void ResetMainCredentials()
        {
            _logger.LogInformation("In ResetMainCredentialsForRegionChange()");
            if (GRDCredentialManager.MainCredentials == null)
            {
                _logger.LogInformation("ResetMainCredentialsForRegionChange(): MainCredentials are already not set.");
                return;
            }
            
            GRDCredentialManager.ClearMainCredentials();
        }

        /// Used to clear all of our current VPN configuration details from user defaults and the keychain
        public async void ClearVpnConfiguration()
        {
            ErrorResponse errorResponse;
            var creds = GRDCredentialManager.MainCredentials;
            if (GRDCredentialManager.MainCredentials != null &&
                !string.IsNullOrEmpty(GRDCredentialManager.MainCredentials.ClientId))
            {
                var clientId = GRDCredentialManager.MainCredentials.UserName;
                (GRDSubscriberCredential subCreds, errorResponse) = await GetValidSubscriberCredentialWithCompletion();
                if (subCreds == null || errorResponse.Message.Equals("PE TOKEN NOT SET")) return; // TJE TODO: CHECK THIS

                GRDGateway gw = new GRDGateway();
                errorResponse = await gw.InvalidateCredentialsForClientId(clientId, creds.ApiAuthToken, creds.HostName, subCreds.Jwt);
                if (errorResponse.IsError)
                {
                    var responseMessage = (HttpResponseMessage)errorResponse.Response;
                    _logger.LogError( $"Failed to invalidate VPN credentials: {responseMessage.ReasonPhrase ?? errorResponse.Message})");
                }
            }
            
            // set user defaults to standard user defaults? TBD
            // remove hostname override - should we allow host name override yet?
            // bool - AppNeedsSelfRepair = false; - TBD
            // Update UI to clear out servers/hosts anything else we reset above
        }

        public void ClearAllGuardianRegistrySettings()
        {
            GRDCredentialManager.ClearMainCredentials();
            GRDKeychain.RemoveGuardianKeychainItems();
        }

        // *******************
        // TJE TODO NOTE 100323 - Instead of Mac version of the flow across multiple similar - will instead use one
        // With intermixed calls for credentials or server selection/setting
        // *******************

        /// <summary>
        /// Used as a helper to calling clients to return the name of the active connection, else null if not
        /// </summary>
        /// <returns>String of Connection Name</returns>
        public bool GetCurrentVPNState(out string connectionName)
        {
            _logger.LogInformation("In GetCurrentVPNState()");
            var state = ClientPipe.GetCurrentVpnConnectionStatus();
            _logger.LogInformation($"GetCurrentVPNState: returned values for state are state: {state.ConnectionState}, entry: '{state.EntryName}'");
            var isConnected  = state.ConnectionState == ConnectionStateEnum.Connected;
            connectionName = state.EntryName;
            return isConnected;
        }
        
        /// Used to create a new VPN connection if an active subscription exists. This is the main function to call when no EAP credentials
        /// or subscriber credentials exist yet and
        /// you want to establish a new connection on a server that is chosen automatically for you.
        /// @param mid block This is a block you can assign for when this process has approached a mid point (a server is selected, subscriber
        /// & eap credentials are generated). optional.
        /// @param completion block This is a block that will return upon completion of the process, if success is TRUE and errorMessage is nil
        /// then we will be successfully connected to a VPN node.
        public void ConfigureFirstTimeUserPostCredential(Action mid, Action<bool, string> completion)
        {
            (string host, string hostLocation, ErrorResponse errorResponse) = _grdServerManager.SelectGuardianHostWithCompletion();
        }

        /// Used to create a new VPN connection if an active subscription exists. This is the main function to call when no VPN credentials or a
        /// Subscriber Credential exist yet and a new connection should be established to a server chosen automatically.
        /// @param protocol The desired transport protocol to use to establish the connection. IKEv2 (builtin) as well as WireGuard via a
        /// PacketTunnelProvider are supported
        /// @param mid block This is a block you can assign for when this process has approached a mid point (a server is selected, subscriber
        /// & eap credentials are generated). optional.
        /// @param completion block This is a block that will return upon completion of the process, if success is TRUE and errorMessage is nil
        /// then we will be successfully connected to a VPN node.
        public async Task<ErrorResponse> ConnectVpnWithNewUserCredentialsThruProtocol(ITransportProvider.TransportProtocol protocol)
        {
            IsBusy = true;
            ErrorResponse? errorResponse = new ErrorResponse();
            
            // CONN#3
            _logger.LogInformation("CONN#3");
            
            // TJE - just go get Subscriber Credentials for IKEv2 for now
            // also - just use first server for my region for now
            // TODO!! - check errorResponse
            errorResponse = await CreateStandaloneCredentialsForTransportProtocol(protocol, 30); // CONN#4-CONN#10
            if (errorResponse.IsError) return errorResponse;
            
            List<GRDCredential> credentials = (List<GRDCredential>)errorResponse.Data;
            
            mainCredential = credentials[0];
            mainCredential.TransportProtocol = protocol;
            mainCredential.MainCredential = true;
            GRDCredentialManager.AddOrUpdateCredential(mainCredential);
            
            // Do connection call here
            errorResponse = await ConnectVpnWithConfiguredCredentials();
            // TODO - Followup here with either result from GRDGatewayAPI.GetServerStatus or actual WCF call over to GuardianFirewallService to make Ras CreateEntry->ConnectEntry calls

            IsBusy = false;
            return errorResponse;
        }

        /// Used subsequently after the first time connection has been successfully made to re-connect to the current host VPN node with mainCredentials
        /// @param completion block This completion block will return a message to display to the user and a status code, if the connection is successful,
        /// the message will be empty.
        public async Task<ErrorResponse> ConnectVpnWithConfiguredCredentials()
        {
            ErrorResponse errorResponse;
            // Need to check if we've set our local copy of credentials and if null then grab from GRDCM
            if (mainCredential == null
                || string.IsNullOrEmpty(mainCredential.HostName)
                || string.IsNullOrEmpty(mainCredential.ApiAuthToken)
                || mainCredential.LastUpdated < GRDCredentialManager.MainCredentials.LastUpdated)
            {
                _logger.LogInformation("GRDVPNHelper: our main credentials not set or older than GRDCredentialsManager's. Syncing now.");
                mainCredential = GRDCredentialManager.MainCredentials;
            }
            
            // CONN#11
            _logger.LogInformation("CONN#11");
            GRDGateway gw = new GRDGateway();
            errorResponse = await gw.GetServerStatus();
            if (errorResponse.IsError)
            {
                errorResponse.SetErrorMessage($"ConfigureAndConnectVPNWithCompletion: GetServerStatus returned: {errorResponse.GetReasonPhrase()}")
                    .SetData(GRDVPNHelperStatusCode.GRDVPNHelperFail);
                return errorResponse;
            }

            if (mainCredential.TransportProtocol != ITransportProvider.TransportProtocol.TransportIKEv2)
            {
                errorResponse.SetException(new InvalidOperationException("MainCredential.TransportProtocol not set!"))
                    .SetErrorMessage("WHY CALLING OLDIKEV2 WITH PROTOCOL NOT SET??");
                return errorResponse;
            }

            var apiAuthToken = mainCredential.ApiAuthToken;
            var eapUsername = mainCredential.UserName;
            var eapPassword = mainCredential.Password;
            if (!string.IsNullOrEmpty(apiAuthToken) && !string.IsNullOrEmpty(eapUsername) &&
                !string.IsNullOrEmpty(eapPassword))
            {
                // Credentials are usable - let's continue
                errorResponse = await _oldStartIKEv2ConnectionWithCompletion();

                return errorResponse;
            }

            // Return error that credentials are bod
            errorResponse.SetException(new Exception("Credentials are not set! VPN Connection not made"));
            
            return errorResponse;
        }

        /// Used to disconnect from the current VPN node
        ///
        /// The sibling to this function - (void) disconnectVPN does not expose various potential errors as it tries to mitigate various OS bugs as
        /// well as trigger race conditions
        /// to provide the expected behavior to begin with.
        ///
        /// This function might be hazardous to your health
        /// - Parameter completion: completion block potentially containing an error message. This completion block may be called multiple times and
        /// could potentially include an error every time
        public async void DisconnectVPN()
        {
            _logger.LogInformation("DISCONN#2");
            _logger.LogInformation("In GRDVPNHelper.DisconnectVPN().");
            var entryName = GetNameOfConnectionEntry();
            _logger.LogInformation($"GRDVPNHelper.DisconnectVPN(): Name of entry to disconnect is '{entryName}'");
            _logger.LogInformation($"GRDVPNHelper.DisconnectVPN(): Calling ClientPipe.DisconnectVPNConnectionAsync() to disconnect '{entryName}'");
            ClientPipe.DisconnectVPNConnection(entryName);
            _logger.LogInformation($"GRDVPNHelper.DisconnectVPN(): Back from ClientPipe.DisconnectVPNConnectionAsync()");
            _logger.LogInformation("DISCONN#3");

        }

        /// There should be no need to call this directly, this is for internal use only.
        public async Task<(GRDSubscriberCredential?, ErrorResponse)> GetValidSubscriberCredentialWithCompletion()
        {
            // CONN#7
            _logger.LogInformation("CONN#7");
            
            ErrorResponse errorResponse;
            GRDSubscriberCredential subCred = GRDSubscriberCredential.GetCurrentStoredSubscriberCredential();
            if (!subCred.IsEmpty && !subCred.IsTokenExpired)
            {
                GRDHousekeepingAPI.LiveGrdCredential = subCred;
                return (GRDHousekeepingAPI.LiveGrdCredential, new ErrorResponse(null));
            }

            var peToken = GRDKeychain.GetPasswordStringForAccount(IGRDKeychain.kKeychainStr_PEToken_Itself);
            if (string.IsNullOrEmpty(peToken))
            {
                errorResponse = new ErrorResponse(Common.kPETOKENNOTSET, null, true);
                return (null, errorResponse);
            }

            GRDHousekeepingAPI houseKeeping = new GRDHousekeepingAPI();
            errorResponse = await houseKeeping.CreateSubscriberCredentialForBundleId(peToken);
            return (GRDHousekeepingAPI.LiveGrdCredential, errorResponse);
        }

        /// Used to create standalone VPN credentials on an automatically chosen host that is valid for a certain number of days. Good for exporting
        /// VPN credentials for use on other devices.
        /// @param protocol The desired transport protocol to use to establish the connection. IKEv2 (builtin) as well as WireGuard via a
        /// PacketTunnelProvider are supported
        /// @param validForDays integer number of days these credentials will be valid for
        /// @param ErrorResponse response that has the Data field that will contain a List<GRDCredentials> collection of credentials upon success
        public async Task<ErrorResponse> CreateStandaloneCredentialsForTransportProtocol(ITransportProvider.TransportProtocol protocol, int validForDays = 30)
        {
            ErrorResponse errorResponse = new ErrorResponse();
            // CONN#4
            _logger.LogInformation("CONN#4");
            
            // TJE - let's do the hosts update right here
            try
            {
                await RegionUtils.GetHostsForRegionKey(RegionUtils.KeyForCurrentlySelectedRegion);
            }
            catch (HttpRequestException hrex)
            {
                _logger.LogError(hrex, $"CreateStandaloneCredentialsForTransportProtocol: http request error while getting region's hosts. Status={hrex.StatusCode}, RequestError={hrex.HttpRequestError}");
                errorResponse = new ErrorResponse().SetException(hrex).SetErrorMessage("HttpRequestError")
                    .SetData(new List<GRDCredential>());
                return errorResponse;
            }
            
            // TJE - This call to SelectGuardianHost is done instead of Mac's version: hostname:[[NSUserDefaults standardUserDefaults]valueForKey:kGRDHostnameOverride]
            GRDServerManager svmgr = new GRDServerManager();
            (var host, var hostDisplay, ErrorResponse error) = svmgr.SelectGuardianHostWithCompletion();
            errorResponse = await CreateStandaloneCredentialsForTransportProtocol(protocol, validForDays, host);
            if (errorResponse.IsError) return errorResponse;
           
            // TJE - adding in host stuff here instead of above in caller
            List<GRDCredential> credentials = (List<GRDCredential>)(errorResponse.Data!);
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
        public async Task<ErrorResponse> CreateStandaloneCredentialsForTransportProtocol(ITransportProvider.TransportProtocol protocol, int days, string hostname)
        {
            // CONN#6
            _logger.LogInformation("CONN#6");
            
            //string errorMessage = "NONE";
            ErrorResponse errorResponse;
            (GRDSubscriberCredential subCreds, errorResponse) = await GetValidSubscriberCredentialWithCompletion();
            if (errorResponse.IsError) return errorResponse;
            // TJE TODO - CHECK IF errorMessage is "PE TOKEN IS NOT SET"
            GRDGateway gateway = new GRDGateway();
            errorResponse = await gateway.RegisterDeviceForTransportProtocol(protocol, hostname, subCreds!.Jwt, days);
            
            // TJE TODO - WHY NOT CHECKING success for false??
            return errorResponse;
        }

        /// Verify that the current main VPN credentials are valid if applicable. A valid Subscriber Credential is automatically obtained and provided
        /// to the VPN node alongside
        /// the credential details. If the device is currently connected and the server indicates that the VPN credentials are no longer valid the
        /// device is automatically
        /// migrated to a new server within the same region
        public void VerifyMainCredentialsWithCompletion(Action<bool, string> completion) {}

        /// Call this to properly assign a GRDRegion to all GRDServerManager instances
        /// @param region the region to select a server from. Pass nil to reset to Automatic region selection mode
        public ErrorResponse SelectRegion(GRDRegion region)
        {
            return new ErrorResponse();
        }

        private async Task<ErrorResponse> _oldStartIKEv2ConnectionWithCompletion()
        {
            ErrorResponse errorResponse = new ErrorResponse();
            
            // CONN#13
            _logger.LogInformation("CONN#13");
            // TJE: - called from configureAndConnectVPNWithCompletion after Server check

            // Make WCF call to GuardianWindowsService to start the connection
            VPNCallParameters vpnValues = new VPNCallParameters
            {
                VpnHostName = mainCredential!.HostName,
                VpnHostDisplay = mainCredential.HostnameDisplayValue,
                EapuserName = mainCredential.UserName,
                Eappassword = mainCredential.Password,
                EntryName = $"Guardian Firewall - {mainCredential.HostnameDisplayValue}"
            };

            _logger.LogInformation("Starting VPN connection...");
            
            try
            {
                await Task.Run(() =>
                {
                    _logger.LogInformation("Calling ClientPipe.StartVPNConnection()...");
                    errorResponse = ClientPipe.StartVPNConnection(vpnValues);
                    if (errorResponse.IsError)
                    {
                        _logger.LogError($"FAILURE to establish VPN connection. ErrorResponse = {errorResponse}");
                    }
                    else
                    {
                        _logger.LogInformation("VPN connection established.");
                    }
                });
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
        /// #85 TODO
        public void clearLocalCache() {}
    
     }
}
