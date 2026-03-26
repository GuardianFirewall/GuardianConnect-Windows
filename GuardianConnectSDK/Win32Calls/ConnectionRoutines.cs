using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using System.Reflection;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.NetworkManagement.Rras;

namespace Win32Calls;

public static class ConnectionRoutines
{
    private static Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;
    public static Microsoft.Extensions.Logging.ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance)
            {
                _logger = StaticLoggerFactory.CreateLogger("ConnectionRoutines");
            }
            return _logger;
        }
    }

    internal const string defaultPhonebookPath = @"C:\ProgramData\Microsoft\Network\Connections\Pbk\rasphone.pbk";

    internal static HRASCONN ActiveConnectionHandle;
    internal static RASCREDENTIALSW ActiveConnectionCredentials;

    public static string ActiveConnectionEntryName { get; private set; } = string.Empty;

    internal static unsafe RASCONNW[] GetRasConnections(out uint cConnections)
    {
        uint cb = 0;
        cConnections = 0;

        // First call to RasEnumConnections to get count of connections and required buffer size

        var retVal = PInvoke.RasEnumConnections(null, ref cb, out cConnections);
        //Logger.LogInformation($"GetRasConnections: First call for size returned {retVal}, cb={cb}, # of Connections = {cConnections}");
        var msg = $"GetRasConnections: First call for size returned {retVal}, cb={cb}, # of Connections = {cConnections}";
        if (cConnections == 0)
        {
            Logger.LogInformation($"GetRasConnections: There are no active RAS connections. Returning empty name, handle and collection array to caller.");
            ActiveConnectionHandle = HRASCONN.Null;
            ActiveConnectionEntryName = "";
            return Array.Empty<RASCONNW>();
        }

        RASCONNW[] connections = new RASCONNW[cConnections];
        var ConnectionsZero= connections[0];
        var pConnectionsZero = &ConnectionsZero;
        pConnectionsZero->dwSize = cb;

        Logger.LogInformation("GetRasConnections: There is an active RAS connection. Preparing to get connection details.");
        //retVal = PInvoke.RasEnumConnections(ref ConnectionsZero, ref cb, out cConnections);
        retVal = PInvoke.RasEnumConnections(pConnectionsZero, ref cb, out cConnections);
        if (retVal != 0)
        {
            Logger.LogError($"GetRasConnections: Call to RasEnumConnections returned NON-SUCCESS value of {retVal}");
            return new RASCONNW[0];
        }

        IntPtr arrayPtr = new IntPtr(pConnectionsZero);
        
        for (int i = 0; i < cConnections; i++)
        {
            connections[i] = (RASCONNW)Marshal.PtrToStructure(arrayPtr, typeof(RASCONNW))!;
            arrayPtr = new IntPtr(arrayPtr.ToInt64() + cb);
        }

        return connections;
    }

    // TJE - this is similar to CheckConnection below except entry name is not known. This is
    // used when we have been started and don't known if or what entry has an
    // active connection. So we iterate through and find ANY active connection
    // and return the handle to be used for notification setup.
    // We'll save entryName elsewhere for UI propagation
    internal static unsafe HRASCONN FindAnyActiveConnection()
    {
        var connections = GetRasConnections(out uint cConnections);
        if (connections.Length == 0)
        {
            Logger.LogError("FindAnyActiveConnection: GetRasConnections returned empty collection");
            return HRASCONN.Null;
        }

        for (int i = 0; i < cConnections; i++)
        {
            var conn = connections[i];
            // We only care about connections that start with "Guardian Firewall -"
            if (!conn.szEntryName.ToString().StartsWith("Guardian Firewall -")) continue;

            var status = new RASCONNSTATUSW
            {
                dwSize = (uint)sizeof(RASCONNSTATUSW)
            };
            var checkResult = GetRasConnectStatus(connections[i].hrasconn, ref status);
            if (checkResult == Utility.CheckConnectionResult.CONNECTED)
            {
                ActiveConnectionEntryName = connections[i].szEntryName.ToString();
                ActiveConnectionHandle = connections[i].hrasconn;
                Logger.LogInformation($"FindAnyActiveConnection: Found active connection for entry {ActiveConnectionEntryName}");
                return connections[i].hrasconn;
            }
        }

        Logger.LogInformation("FindAnyActiveConnection: No active Guardian Firewall IKEv2 connections found under Ras networking");

        return HRASCONN.Null;
    }

    public static string GetEntryNameOfActiveConnection()
    {
        var activehandle = FindAnyActiveConnection();
        return ActiveConnectionEntryName;
    }

    public static bool IsAnyConnectionActive(out string entryNameOut)
    {
        entryNameOut = "";
        var whetherWeAreConnected = FindAnyActiveConnection() != HRASCONN.Null;
        entryNameOut = ActiveConnectionEntryName;
        return whetherWeAreConnected;
    }

    public static unsafe ErrorResponse CreateOrUpdateEntry(string entryName, string hostName, string userName, string password, string phonebookPath = defaultPhonebookPath)
    {
        RASENTRYW entry = new RASENTRYW
        {
            dwSize = (uint)sizeof(RASENTRYW),
            dwfOptions = PInvoke.RASEO_RemoteDefaultGateway | PInvoke.RASEO_RequireEAP | PInvoke.RASEO_PreviewDomain | PInvoke.RASEO_ShowDialingProgress,
            szLocalPhoneNumber = hostName,
            dwfNetProtocols = PInvoke.RASNP_Ip | PInvoke.RASNP_Ipv6,
            dwFramingProtocol = PInvoke.RASFP_Ppp,
            szDeviceType = PInvoke.RASDT_Vpn,
            szDeviceName = "WAN Miniport (IKEv2)",
            dwType = PInvoke.RASET_Vpn,
            dwEncryptionType = PInvoke.ET_Optional,
            dwVpnStrategy = PInvoke.VS_Ikev2Only,
            dwfOptions2 = PInvoke.RASEO2_DontNegotiateMultilink | PInvoke.RASEO2_ReconnectIfDropped |
                          PInvoke.RASEO2_IPv6RemoteDefaultGateway | PInvoke.RASEO2_CacheCredentials,
            dwRedialCount = 3,
            dwRedialPause = 60,
            // this maps to "Type of sign-in info" => "User name and password"
            dwCustomAuthKey = 26
        };

        Logger.LogInformation($"CreateOrUpdateEntry: Entry values are '{entryName}', '{entry.szLocalPhoneNumber}'");

        uint dwRet = PInvoke.RasSetEntryProperties(null, entryName, entry, entry.dwSize, null, 0);
        if (dwRet != 0)
        {
            Logger.LogError($"CreateOrUpdateEntry: Call to RasSetEntryProperties returned NON-SUCCESS value of {dwRet}");
            return new ErrorResponse("NON-SUCCESS return from call to RasSetEntryProperties", null, true, null, dwRet);
        }

        RASCREDENTIALSW credentials = new RASCREDENTIALSW
        {
            dwMask = PInvoke.RASCM_UserName | PInvoke.RASCM_Password,
            szUserName = userName,
            szPassword = password,
            dwSize = (uint)sizeof(RASCREDENTIALSW)
        };

        dwRet = PInvoke.RasSetCredentials(null, entryName, credentials, false);
        if (dwRet != 0)
        {
            Logger.LogError($"CreateOrUpdateEntry: Call to RasSetCredentials returned NON-SUCCESS value of {dwRet:X8}");
            return new ErrorResponse("NON-SUCCESS return from call to RasSetCredentials", null, true, null, dwRet);
        }

#if DEBUG
        Logger.LogDebug($"CreateOrUpdateEntry: Credentials values set are '{credentials.szUserName}', '{credentials.szPassword}'");
#endif

        bool entryWasWritten = PInvoke.WritePrivateProfileString(entryName, "NumCustomPolicy", "1", phonebookPath);
        if (!entryWasWritten)
        {
            var error = Marshal.GetLastWin32Error();
            Logger.LogError($"CreateOrUpdateEntry: Call to WritePrivateProfileString for NumCustomPolicy returned false. LastWin32Error = {error}");
            return new ErrorResponse("Call to WritePrivateProfileString for NumCustomPolicy returned false", null, true, null, error);
        }

        entryWasWritten = PInvoke.WritePrivateProfileString(entryName, "CustomIPSecPolicies",
            "030000000400000002000000050000000200000000000000", phonebookPath);
        /* given by BC/Brave - the 5 in the middle here could be the ECP384 curve specifier
                   "030000000400000005000000050000000200000000000000", 
        */
        if (!entryWasWritten)
        {
            var error = Marshal.GetLastWin32Error();
            Logger.LogError($"CreateOrUpdateEntry: Call to WritePrivateProfileString for CustomIPSecPolicies returned false. LastWin32Error = {error}");
            return new ErrorResponse("Call to WritePrivateProfileString returned false", null, true, null, error);
        }

        ActiveConnectionEntryName = entryName;
        ActiveConnectionCredentials = credentials;
        return new ErrorResponse("Success", null, false, null, 0);
    }

    public static unsafe ErrorResponse ConnectEntry()
    {
        Logger.LogInformation($"In ConnectEntry - Entry name = {ActiveConnectionEntryName}");

        // First - check to see if our connection is already active
        // We now only use our previously stored handle if it is valid, instead of calling FindAnyActiveConnection which uses RasEnumConnections
        // We don't need to do that extra work
        RASCONNSTATUSW status = new RASCONNSTATUSW
        {
            dwSize = (uint)sizeof(RASCONNSTATUSW)
        };

        if (ActiveConnectionHandle!= IntPtr.Zero)
        {
            Utility.CheckConnectionResult checkResult = GetRasConnectStatus(ActiveConnectionHandle, ref status);
            if (checkResult == Utility.CheckConnectionResult.CONNECTED)
            {
                Logger.LogInformation("In ConnectEntry - connection already active");
                return new ErrorResponse("Connection already active", null, false, null, 0);
            }
        }

        // Need to get credentials and stuff in the username and password
        var retVal = PInvoke.RasGetCredentials(null, ActiveConnectionEntryName, ref ActiveConnectionCredentials);
        if (retVal != 0)
        {
            Logger.LogError($"ConnectEntry: Call to RasGetCredentials returned NON-SUCCESS value of {retVal}");
            return new ErrorResponse("NON-SUCCESS return from call to RasGetCredentials", null, true, null, retVal);
        }

        RASDIALPARAMSW rasdialparamsw = new RASDIALPARAMSW
        {
            szEntryName = ActiveConnectionEntryName,
            szDomain = "*",
            szUserName = ActiveConnectionCredentials.szUserName,
            szPassword = ActiveConnectionCredentials.szPassword
        };
        rasdialparamsw.dwSize = (uint)sizeof(RASDIALPARAMSW);

        ActiveConnectionHandle = HRASCONN.Null; // Reset our handle to zero before dialing
        fixed (HRASCONN* pAcH = &ActiveConnectionHandle)
        {
            retVal = PInvoke.RasDial(null, null, &rasdialparamsw, 0, null, pAcH);

            if (retVal != 0)
            {
                Logger.LogError($"ConnectEntry: Call to RasDial returned NON-SUCCESS value of {retVal}");
                return new ErrorResponse("NON-SUCCESS return from call to RasDial", null, true, null, retVal);
            }
        }

        // Update Filters to shape traffic as needed
        Logger.LogInformation("ConnectEntry: Calling VpnDnsFilteringHandler.UpdateFiltersState to turn on traffic filtering...");
        VpnDnsFilteringHandler.UpdateFiltersState(ActiveConnectionEntryName);

        Logger.LogInformation("ConnectEntry: exiting...");
        return new ErrorResponse();
    }

    public static bool DisconnectEntryAndRemove()
    {
        Logger.LogInformation($"ConnectionRoutines.DisconnectEntryAndRemove: ActiveConnectionHandle null? {(ActiveConnectionHandle == HRASCONN.Null)}, ActiveConnectionEntryName = '{ActiveConnectionEntryName}'");
        bool disconnectResult = false;
        var connectionResult = CheckConnection(ActiveConnectionEntryName, ref ActiveConnectionHandle);
        Logger.LogInformation($"ConnectionRoutines.DisconnectEntryAndRemove: checking connection returned {connectionResult}");
        if (connectionResult == Utility.CheckConnectionResult.CONNECTED && ActiveConnectionHandle != HRASCONN.Null)
        {
            var retVal = PInvoke.RasHangUp(ActiveConnectionHandle);
            if (retVal != 0)
            {
                Logger.LogError($"DisconnectEntryAndRemove: Call to RasHangUp returned NON-SUCCESS value of {retVal:X8}");
                disconnectResult = false;
            }
            else
            {
                Logger.LogInformation("DisconnectEntryAndRemove: Successful return from call to RasHangup. Calling UpdateFiltersState to remove all traffic filters...");

                // Update Filters to shape traffic as needed
                VpnDnsFilteringHandler.UpdateFiltersState(ActiveConnectionEntryName);
                ActiveConnectionHandle = HRASCONN.Null;
                disconnectResult = true;
                ActiveConnectionEntryName = "";
            }
        }
        else
        {
            // Not connected, so nothing to do
            Logger.LogInformation($"Entry '{ActiveConnectionEntryName} is not in a CONNECTED state. Its state is {connectionResult}");
            disconnectResult = true;
        }

        // Now remove all entries in phonebook - for now we only have Guardian Firewall entries
        //RemoveAnyGuardianEntries();
        RemoveAllRasEntries();

        return disconnectResult;
    }

    public static Utility.CheckConnectionResult CheckConnection(string entry_name)
    {
        HRASCONN waste = HRASCONN.Null;
        return CheckConnection(entry_name, ref waste);
    }

    internal static unsafe Utility.CheckConnectionResult CheckConnection(string entryName, ref HRASCONN handleOut)
    {
        Utility.CheckConnectionResult result = Utility.CheckConnectionResult.DISCONNECTED;
        RASCONNSTATUSW status = new RASCONNSTATUSW
        {
            dwSize = (uint)sizeof(RASCONNSTATUSW)
        };

        var connections = GetRasConnections(out uint cConnections);
        if (connections.Length == 0)
        {
            Logger.LogError("CheckConnection: GetRasConnections returned empty collection");
            return result;
        }

        for (int i = 0; i < cConnections; i++)
        {
            if (connections[i].szEntryName.ToString() != entryName) continue;
            result = GetRasConnectStatus(connections[i].hrasconn, ref status);
            switch (result)
            {
                case Utility.CheckConnectionResult.CONNECTED:
                    handleOut = connections[i].hrasconn;
                    ActiveConnectionEntryName = entryName;
                    ActiveConnectionHandle = handleOut;
                    break;
                case Utility.CheckConnectionResult.CONNECTING:
                case Utility.CheckConnectionResult.DISCONNECTING:
                    handleOut = HRASCONN.Null;
                    break;
                case Utility.CheckConnectionResult.DISCONNECTED:
                case Utility.CheckConnectionResult.CONNECT_FAILED:
                    handleOut = HRASCONN.Null;
                    // TODO: Do we want to clear out the saved entry name and handle here?
                    break;
                case Utility.CheckConnectionResult.Uninitialized:
                    handleOut = HRASCONN.Null;
                    break;
            }
        }

        return result;
    }

    internal static Utility.CheckConnectionResult GetRasConnectStatus(HRASCONN h_ras_conn, ref RASCONNSTATUSW lp_ras_status)
    {
        var retVal = PInvoke.RasGetConnectStatus(h_ras_conn, ref lp_ras_status);
        if (retVal != 0)
        {
            Logger.LogError($"GetRasConnectStatus: Call to RasGetConnectStatus returned NON-SUCCESS value of {retVal}");
            return Utility.CheckConnectionResult.DISCONNECTED;
        }

        return lp_ras_status.rasconnstate switch
        {
            RASCONNSTATE.RASCS_Connected => Utility.CheckConnectionResult.CONNECTED,
            RASCONNSTATE.RASCS_Disconnected => Utility.CheckConnectionResult.DISCONNECTED,
            _ => Utility.CheckConnectionResult.Uninitialized
        };
    }

#if NOTWORKING
    internal static unsafe void RemoveAnyGuardianEntries()
    {
        try
        {
            //var entries = GetRasConnections(out uint numberOfConnections);
            //var entries = PInvoke.RasEnumEntries(null, null, out uint numberOfConnections);
            //
            uint entriesBufferSize = 0;
            uint numberOfConnections = 0;
            RASENTRYNAMEW[] entries = Array.Empty<RASENTRYNAMEW>();

            // First call to get required buffer size and number of entries
            uint ret = PInvoke.RasEnumEntries(null, null, null, ref entriesBufferSize, out numberOfConnections);
            if (numberOfConnections == 0)
            {
                Logger.LogInformation("RemoveAnyGuardianEntries: No RAS entries found in phonebook.");
                return;
            }
            if (ret == PInvoke.ERROR_BUFFER_TOO_SMALL && entriesBufferSize > 0)
            {
                entries = new RASENTRYNAMEW[entriesBufferSize / (uint)Marshal.SizeOf<RASENTRYNAMEW>()];
                var pEntries = &entries;
                for (int i = 0; i < entries.Length; i++)
                {
                    entries[i].dwSize = (uint)Marshal.SizeOf<RASENTRYNAMEW>();
                }
                //ret = PInvoke.RasEnumEntries(null, null, pEntries, ref entriesBufferSize, out numberOfConnections);
            }
            //
            Logger.LogInformation($"Number of RAS connections = {numberOfConnections}");
            for (int i = 0; i < numberOfConnections; i++)
            {
                var conn = entries[i];
                // We only care about connections that start with "Guardian Firewall -"
                if (!conn.szEntryName.ToString().StartsWith("Guardian Firewall -")) continue;

                Logger.LogInformation($"RemoveAnyGuardianEntries: Removing entry '{conn.szEntryName}' from phonebook...");
                PInvoke.RasDeleteEntry(null, conn.szEntryName.ToString());
            }
        }
        catch (Exception e)
        {
            Log.Error(e, $"RemoveAnyGuardianEntries: Exeption thrown {e.Message}");
        }
    }
#endif

    internal static unsafe void RemoveAllRasEntries()
    {
        try
        {
            uint entriesBufferSize = 0;
            uint numberOfEntries = 0;

            // First call to get required buffer size and number of entries
            uint ret = PInvoke.RasEnumEntries(null, null, null, ref entriesBufferSize, out numberOfEntries);
            if (numberOfEntries == 0)
            {
                Logger.LogInformation("RemoveAllRasEntries: No RAS entries found in phonebook.");
                return;
            }
            if (ret != PInvoke.ERROR_BUFFER_TOO_SMALL || entriesBufferSize == 0)
            {
                Logger.LogError($"RemoveAllRasEntries: Unexpected return value from RasEnumEntries: {ret}, buffer size: {entriesBufferSize}");
                return;
            }

            RASENTRYNAMEW[] entries = new RASENTRYNAMEW[numberOfEntries];
            fixed (RASENTRYNAMEW* pEntries = entries)
            {
                for (int i = 0; i < numberOfEntries; i++)
                {
                    pEntries[i].dwSize = (uint)Marshal.SizeOf<RASENTRYNAMEW>();
                }

                ret = PInvoke.RasEnumEntries(null, null, pEntries, ref entriesBufferSize, out numberOfEntries);
                if (ret != 0)
                {
                    Logger.LogError($"RemoveAllRasEntries: RasEnumEntries failed with error {ret}");
                    return;
                }

                for (int i = 0; i < numberOfEntries; i++)
                {
                    string entryName = pEntries[i].szEntryName.ToString();
                    Logger.LogInformation($"RemoveAllRasEntries: Deleting entry '{entryName}'...");
                    uint deleteRet = PInvoke.RasDeleteEntry(null, entryName);
                    if (deleteRet != 0)
                    {
                        Logger.LogError($"RemoveAllRasEntries: RasDeleteEntry failed for '{entryName}' with error {deleteRet}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"RemoveAllRasEntries: Exception thrown: {ex.Message}");
        }
    }
}
    