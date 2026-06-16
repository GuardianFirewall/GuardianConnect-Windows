using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.Rras;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Win32Calls;

public static class ConnectionRoutines
{
    private static ILogger _logger = NullLogger.Instance;

    public static ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance) _logger = StaticLoggerFactory.CreateLogger("ConnectionRoutines");
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
        var msg =
            $"GetRasConnections: First call for size returned {retVal}, cb={cb}, # of Connections = {cConnections}";
        if (cConnections == 0)
        {
            Logger.LogInformation(
                "GetRasConnections: There are no active RAS connections. Returning empty name, handle and collection array to caller.");
            ActiveConnectionHandle = HRASCONN.Null;
            ActiveConnectionEntryName = "";
            return Array.Empty<RASCONNW>();
        }

        var connections = new RASCONNW[cConnections];
        var ConnectionsZero = connections[0];
        var pConnectionsZero = &ConnectionsZero;
        pConnectionsZero->dwSize = cb;

        Logger.LogInformation(
            "GetRasConnections: There is an active RAS connection. Preparing to get connection details.");
        //retVal = PInvoke.RasEnumConnections(ref ConnectionsZero, ref cb, out cConnections);
        retVal = PInvoke.RasEnumConnections(pConnectionsZero, ref cb, out cConnections);
        if (retVal != 0)
        {
            Logger.LogError($"GetRasConnections: Call to RasEnumConnections returned NON-SUCCESS value of {retVal}");
            return new RASCONNW[0];
        }

        var arrayPtr = new IntPtr(pConnectionsZero);

        for (var i = 0; i < cConnections; i++)
        {
            connections[i] = (RASCONNW)Marshal.PtrToStructure(arrayPtr, typeof(RASCONNW))!;
            arrayPtr = new IntPtr(arrayPtr.ToInt64() + cb);
        }

        return connections;
    }

    // This is similar to CheckConnection below except entry name is not known. This is
    // used when we have been started and don't know if or what entry has an
    // active connection. So we iterate through and find ANY active connection
    // and return the handle to be used for notification setup.
    // We'll save entryName elsewhere for UI propagation
    internal static unsafe HRASCONN FindAnyActiveConnection()
    {
        var connections = GetRasConnections(out var cConnections);
        if (connections.Length == 0)
        {
            Logger.LogError("FindAnyActiveConnection: GetRasConnections returned empty collection");
            return HRASCONN.Null;
        }

        for (var i = 0; i < cConnections; i++)
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
                Logger.LogInformation(
                    $"FindAnyActiveConnection: Found active connection for entry {ActiveConnectionEntryName}");
                return connections[i].hrasconn;
            }
        }

        Logger.LogInformation(
            "FindAnyActiveConnection: No active Guardian Firewall IKEv2 connections found under Ras networking");

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

    public static unsafe ErrorResponse CreateOrUpdateEntry(string entryName, string hostName, string userName,
        string password, string phonebookPath = defaultPhonebookPath)
    {
        var entry = new RASENTRYW
        {
            dwSize = (uint)sizeof(RASENTRYW),
            dwfOptions = PInvoke.RASEO_RemoteDefaultGateway | PInvoke.RASEO_RequireEAP | PInvoke.RASEO_PreviewDomain |
                         PInvoke.RASEO_ShowDialingProgress,
            szLocalPhoneNumber = hostName,
            dwfNetProtocols = PInvoke.RASNP_Ip | PInvoke.RASNP_Ipv6,
            dwFramingProtocol = PInvoke.RASFP_Ppp,
            szDeviceType = PInvoke.RASDT_Vpn,
            szDeviceName = "WAN Miniport (IKEv2)",
            dwType = PInvoke.RASET_Vpn,
            dwEncryptionType = PInvoke.ET_RequireMax,
            dwVpnStrategy = PInvoke.VS_Ikev2Only,
            dwfOptions2 = PInvoke.RASEO2_DontNegotiateMultilink | PInvoke.RASEO2_ReconnectIfDropped |
                          PInvoke.RASEO2_IPv6RemoteDefaultGateway | PInvoke.RASEO2_CacheCredentials,
            dwRedialCount = 3,
            dwRedialPause = 60,
            // this maps to "Type of sign-in info" => "User name and password"
            dwCustomAuthKey = 26
        };

        Logger.LogInformation($"CreateOrUpdateEntry: Entry values are '{entryName}', '{entry.szLocalPhoneNumber}'");

        var dwRet = PInvoke.RasSetEntryProperties(null, entryName, entry, entry.dwSize, null, 0);
        if (dwRet != 0)
        {
            Logger.LogError(
                $"CreateOrUpdateEntry: Call to RasSetEntryProperties returned NON-SUCCESS value of {dwRet}");
            return new ErrorResponse("NON-SUCCESS return from call to RasSetEntryProperties", null, true, null, dwRet);
        }

        var credentials = new RASCREDENTIALSW
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
        Logger.LogDebug(
            $"CreateOrUpdateEntry: Credentials values set are '{credentials.szUserName}', '{credentials.szPassword}'");
#endif

        bool entryWasWritten = PInvoke.WritePrivateProfileString(entryName, "NumCustomPolicy", "1", phonebookPath);
        if (!entryWasWritten)
        {
            var error = Marshal.GetLastWin32Error();
            Logger.LogError(
                $"CreateOrUpdateEntry: Call to WritePrivateProfileString for NumCustomPolicy returned false. LastWin32Error = {error}");
            return new ErrorResponse("Call to WritePrivateProfileString for NumCustomPolicy returned false", null, true,
                null, error);
        }

        // CustomIPSecPolicies = 6 little-endian DWORDs. AES-256-GCM / SHA-384 / ECP-384.
        //   [0] dwIntegrityMethod         = 3  SHA-384      (IKE/MM integrity)
        //   [1] dwEncryptionMethod        = 4  AES-256      (IKE/MM cipher)
        //   [2] dwCipherTransformConstant = 5  AES-256-GCM  (ESP/QM data cipher)
        //   [3] dwAuthTransformConstant   = 8  AES-256-GCM  (ESP/QM auth)
        //   [4] dwPfsGroup                = 5  ECP-384      (QM PFS)
        //   [5] dwDhGroup                 = 5  ECP-384      (IKE/MM key exchange)
        // IMPORTANT: the CLIENT phonebook encodes GCM-256 as cipher=5 / auth=8 — the OPPOSITE of
        // the MS-RRASM *server* enum (ROUTER_CUSTOM_IKEv2_POLICY_0 lists GCM-256 cipher=8/auth=5).
        // Using the server values here produced FWP_E_INVALID_ENUMERATOR (0x8032001D) at RasDial.
        // This hex is what Set-VpnConnectionIPsecConfiguration (the authoritative client API)
        // emits for -CipherTransformConstants GCMAES256 -AuthenticationTransformConstants GCMAES256
        // -EncryptionMethod AES256 -IntegrityCheckMethod SHA384 -DHGroup ECP384 -PfsGroup ECP384.
        // NOTE: with ET_RequireMax the dial fails (no downgrade) if the gateway can't offer this
        // suite — verify a successful SA with Get-NetIPsecQuickModeSA.
        entryWasWritten = PInvoke.WritePrivateProfileString(entryName, "CustomIPSecPolicies",
            "030000000400000005000000080000000500000005000000", phonebookPath);
        if (!entryWasWritten)
        {
            var error = Marshal.GetLastWin32Error();
            Logger.LogError(
                $"CreateOrUpdateEntry: Call to WritePrivateProfileString for CustomIPSecPolicies returned false. LastWin32Error = {error}");
            return new ErrorResponse("Call to WritePrivateProfileString returned false", null, true, null, error);
        }

        // EAP-MSCHAPv2 user data (iOS parity). See ConfigureEapMschapV2UserData.
        // Non-fatal in the prototype: if it fails we log and continue so we can
        // compare a dial WITH vs WITHOUT the EAP user-data blob.
        var eapResp = ConfigureEapMschapV2UserData(entryName, userName, password, phonebookPath);
        if (eapResp.IsError)
            Logger.LogWarning(
                $"CreateOrUpdateEntry: ConfigureEapMschapV2UserData failed (continuing): {eapResp.Message}");

        ActiveConnectionEntryName = entryName;
        ActiveConnectionCredentials = credentials;
        return new ErrorResponse("Success", null, false, null, 0);
    }

    /// <summary>
    /// PROTOTYPE — iOS parity for the IKEv2 EAP user-auth phase.
    ///
    /// iOS (<c>GRDVPNHelper._prepareIKEv2ParametersForServer</c>) sets
    /// <c>useExtendedAuthentication = YES</c> with <c>username</c> +
    /// <c>passwordReference</c>: the gateway authenticates with its (LetsEncrypt)
    /// certificate — validated via <c>serverCertificateCommonName = hostname</c> —
    /// and the USER authenticates with EAP-MSCHAPv2.
    ///
    /// On Windows the gateway-certificate validation is already automatic: the
    /// IKEv2 client validates the server cert against the dialed hostname
    /// (<c>szLocalPhoneNumber</c>) and the machine Trusted Root store (which carries
    /// the LetsEncrypt ISRG root), so there is no Windows knob to mirror
    /// <c>serverCertificateCommonName</c> — it happens by virtue of dialing by name.
    ///
    /// What was MISSING is the EAP user-auth half. The entry sets
    /// <c>RASEO_RequireEAP</c> + <c>dwCustomAuthKey = 26</c> (EAP-MSCHAPv2), but the
    /// credentials were only supplied the legacy way (<c>RasSetCredentials</c> /
    /// <c>RASDIALPARAMS</c>). Under EAP, the EAPHost MSCHAPv2 peer reads its
    /// credentials from the per-entry EAP user-data blob; with none set, a
    /// non-interactive SYSTEM-service dial has no way to answer the EAP challenge
    /// and the auth fails in a way that looks like an MSCHAPv2/server problem.
    ///
    /// This asks EAPHost to materialise the EAP user-identity blob for the entry's
    /// configured EAP type (26) NON-INTERACTIVELY — seeded from the credentials we
    /// just stored via RasSetCredentials — and writes it back with
    /// <c>RasSetEapUserData</c>, which is the EAP analog of iOS handing
    /// username/passwordReference to NEVPNProtocolIKEv2.
    ///
    /// NOTE: untested against a live gateway — validate end-to-end with a real dial
    /// and confirm a Quick-Mode SA via <c>Get-NetIPsecQuickModeSA</c>. The exact
    /// non-interactive flag / blob round-trip is the part to verify.
    /// </summary>
    private static unsafe ErrorResponse ConfigureEapMschapV2UserData(
        string entryName, string userName, string password, string phonebookPath)
    {
        // RASEAPF_NonInteractive (0x2): never raise UI — required in a service.
        const uint RASEAPF_NonInteractive = 0x00000002;

        RASEAPUSERIDENTITYW* pIdentity = null;
        var rc = PInvoke.RasGetEapUserIdentity(
            phonebookPath, entryName, RASEAPF_NonInteractive, default, out pIdentity);
        if (rc != 0 || pIdentity is null)
        {
            // EAPHost wouldn't synthesize the identity blob without UI. Fall back to
            // installing an explicit EAP-MSCHAPv2 *configuration* on the entry so the
            // method is fully described and the dial can proceed from stored creds.
            Logger.LogWarning(
                "ConfigureEapMschapV2UserData: RasGetEapUserIdentity failed (0x{Rc:X8}); falling back to XML EAP config.",
                rc);
            return ConfigureEapViaXmlConfig(entryName, phonebookPath);
        }

        try
        {
            // pbEapInfo/dwSizeofEapInfo is the opaque EAP-method blob EAPHost built
            // for EAP type 26 using the entry's stored credentials. Hand it straight
            // back as this entry's EAP user data so RasDial can answer the challenge
            // without a UI prompt.
            if (pIdentity->dwSizeofEapInfo == 0)
            {
                return new ErrorResponse(
                    "RasGetEapUserIdentity returned an empty EAP blob", null, true, null, 0);
            }

            // hToken = null: apply to the entry (per-machine phonebook), not a
            // specific logon token. Arg 4 is the first byte of the inline blob;
            // dwSizeofEapInfo bounds it.
            rc = PInvoke.RasSetEapUserData(
                null, phonebookPath, entryName,
                in pIdentity->pbEapInfo[0], pIdentity->dwSizeofEapInfo);
            if (rc != 0)
            {
                return new ErrorResponse(
                    $"RasSetEapUserData failed (0x{rc:X8})", null, true, null, rc);
            }
        }
        finally
        {
            PInvoke.RasFreeEapUserIdentity(pIdentity);
        }

        Logger.LogInformation(
            "ConfigureEapMschapV2UserData: EAP-MSCHAPv2 user data set for entry '{Entry}' (user '{User}')",
            entryName, userName);
        return new ErrorResponse();
    }

    // eappcfg.dll EAPHost config APIs — converting an EapHostConfig XML document to
    // the binary config blob that RAS stores. Declared by hand (not CsWin32) because
    // the XML node is a live MSXML COM object we marshal as IUnknown.
    [DllImport("eappcfg.dll", CharSet = CharSet.Unicode)]
    private static extern uint EapHostPeerConfigXml2Blob(
        uint dwFlags, IntPtr pConfigDoc, out uint pdwSizeOfConfigOut, out IntPtr ppConfigOut, out IntPtr ppEapError);

    [DllImport("eappcfg.dll")]
    private static extern void EapHostPeerFreeMemory(IntPtr pData);

    [DllImport("eappcfg.dll")]
    private static extern void EapHostPeerFreeErrorMemory(IntPtr pEapError);

    /// <summary>
    /// PROTOTYPE FALLBACK for <see cref="ConfigureEapMschapV2UserData"/>.
    ///
    /// When EAPHost won't synthesize the user-identity blob non-interactively, install
    /// an explicit EAP-MSCHAPv2 (<c>&lt;Type&gt;26&lt;/Type&gt;</c>) configuration on the
    /// entry instead. We build the standard <c>EapHostConfig</c> XML, convert it to the
    /// binary config blob with <c>EapHostPeerConfigXml2Blob</c>, and store it with
    /// <c>RasSetCustomAuthData</c> — the same blob <c>Set-VpnConnection -EapConfigXmlStream</c>
    /// writes. With the method fully described, RasDial can answer EAP-MSCHAPv2 from the
    /// credentials stored via RasSetCredentials, no UI prompt.
    ///
    /// Note: EAP-MSCHAPv2's own config surface is minimal (no cert-validation knobs — that
    /// is PEAP). The IKEv2 gateway-cert validation that mirrors iOS's
    /// <c>serverCertificateCommonName</c> is handled at the IKEv2 layer (dialed hostname +
    /// machine Trusted Root store), not here.
    ///
    /// UNTESTED. Verify the eappcfg.dll export name/host and the MSXML marshaling on a real
    /// machine; confirm a successful dial + Quick-Mode SA.
    /// </summary>
    private static unsafe ErrorResponse ConfigureEapViaXmlConfig(string entryName, string phonebookPath)
    {
        var xml = BuildEapMschapV2ConfigXml();

        // Load the XML into an MSXML DOM (late-bound COM) and hand its IUnknown to
        // EapHostPeerConfigXml2Blob, which QIs it for IXMLDOMNode.
        object? domDoc = null;
        IntPtr pUnk = IntPtr.Zero, pBlob = IntPtr.Zero, pErr = IntPtr.Zero;
        try
        {
            var domType = Type.GetTypeFromProgID("MSXML2.DOMDocument.6.0");
            if (domType is null)
                return new ErrorResponse("MSXML2.DOMDocument.6.0 not available", null, true, null, 0);

            domDoc = Activator.CreateInstance(domType);
            if ((bool)domType.InvokeMember("loadXML",
                    System.Reflection.BindingFlags.InvokeMethod, null, domDoc, new object[] { xml })! == false)
                return new ErrorResponse("MSXML failed to parse the EAP config XML", null, true, null, 0);

            pUnk = Marshal.GetIUnknownForObject(domDoc);

            var rc = EapHostPeerConfigXml2Blob(0, pUnk, out var blobSize, out pBlob, out pErr);
            if (rc != 0 || pBlob == IntPtr.Zero || blobSize == 0)
                return new ErrorResponse($"EapHostPeerConfigXml2Blob failed (0x{rc:X8})", null, true, null, rc);

            var blob = new byte[blobSize];
            Marshal.Copy(pBlob, blob, 0, (int)blobSize);

            // RasSetCustomAuthData(pszPhonebook, pszEntry, pbCustomAuthData, dwSizeofCustomAuthData).
            // This CsWin32 overload is the raw form: PCWSTR strings + byte* blob.
            uint setRc;
            fixed (char* pPb = phonebookPath)
            fixed (char* pEntry = entryName)
            fixed (byte* pBlobBytes = blob)
            {
                setRc = PInvoke.RasSetCustomAuthData(
                    new PCWSTR(pPb), new PCWSTR(pEntry), pBlobBytes, blobSize);
            }
            if (setRc != 0)
                return new ErrorResponse($"RasSetCustomAuthData failed (0x{setRc:X8})", null, true, null, setRc);

            Logger.LogInformation(
                "ConfigureEapViaXmlConfig: installed EAP-MSCHAPv2 XML config ({Size} bytes) on entry '{Entry}'",
                blobSize, entryName);
            return new ErrorResponse();
        }
        catch (Exception ex)
        {
            return new ErrorResponse($"ConfigureEapViaXmlConfig threw: {ex.Message}", null, true, null, 0);
        }
        finally
        {
            if (pErr != IntPtr.Zero) EapHostPeerFreeErrorMemory(pErr);
            if (pBlob != IntPtr.Zero) EapHostPeerFreeMemory(pBlob);
            if (pUnk != IntPtr.Zero) Marshal.Release(pUnk);
            if (domDoc is not null) Marshal.FinalReleaseComObject(domDoc);
        }
    }

    /// <summary>
    /// EapHostConfig XML for EAP-MSCHAPv2 (EAP type 26). This is the minimal valid
    /// document EAPHost accepts for the method; MSCHAPv2 carries only
    /// UseWinLogonCredentials (false — we supply our own creds via RasSetCredentials).
    /// </summary>
    private static string BuildEapMschapV2ConfigXml() =>
        """
        <EapHostConfig xmlns="http://www.microsoft.com/provisioning/EapHostConfig">
          <EapMethod>
            <Type xmlns="http://www.microsoft.com/provisioning/EapCommon">26</Type>
            <VendorId xmlns="http://www.microsoft.com/provisioning/EapCommon">0</VendorId>
            <VendorType xmlns="http://www.microsoft.com/provisioning/EapCommon">0</VendorType>
            <AuthorId xmlns="http://www.microsoft.com/provisioning/EapCommon">0</AuthorId>
          </EapMethod>
          <Config xmlns="http://www.microsoft.com/provisioning/EapHostConfig">
            <Eap xmlns="http://www.microsoft.com/provisioning/BaseEapConnectionPropertiesV1">
              <Type>26</Type>
              <EapType xmlns="http://www.microsoft.com/provisioning/MsChapV2ConnectionPropertiesV1">
                <UseWinLogonCredentials>false</UseWinLogonCredentials>
              </EapType>
            </Eap>
          </Config>
        </EapHostConfig>
        """;

    public static unsafe ErrorResponse ConnectEntry()
    {
        Logger.LogInformation($"In ConnectEntry - Entry name = {ActiveConnectionEntryName}");

        // First - check to see if our connection is already active
        // We now only use our previously stored handle if it is valid, instead of calling FindAnyActiveConnection which uses RasEnumConnections
        // We don't need to do that extra work
        var status = new RASCONNSTATUSW
        {
            dwSize = (uint)sizeof(RASCONNSTATUSW)
        };

        if (ActiveConnectionHandle != IntPtr.Zero)
        {
            var checkResult = GetRasConnectStatus(ActiveConnectionHandle, ref status);
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

        var rasdialparamsw = new RASDIALPARAMSW
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
        Logger.LogInformation(
            "ConnectEntry: Calling VpnDnsFilteringHandler.UpdateFiltersState to turn on traffic filtering...");
        VpnDnsFilteringHandler.UpdateFiltersState(ActiveConnectionEntryName);

        Logger.LogInformation("ConnectEntry: exiting...");
        return new ErrorResponse();
    }

    public static bool DisconnectEntryAndRemove()
    {
        Logger.LogInformation(
            $"ConnectionRoutines.DisconnectEntryAndRemove: ActiveConnectionHandle null? {ActiveConnectionHandle == HRASCONN.Null}, ActiveConnectionEntryName = '{ActiveConnectionEntryName}'");
        var disconnectResult = false;
        var connectionResult = CheckConnection(ActiveConnectionEntryName, ref ActiveConnectionHandle);
        Logger.LogInformation(
            $"ConnectionRoutines.DisconnectEntryAndRemove: checking connection returned {connectionResult}");
        if (connectionResult == Utility.CheckConnectionResult.CONNECTED && ActiveConnectionHandle != HRASCONN.Null)
        {
            var retVal = PInvoke.RasHangUp(ActiveConnectionHandle);
            if (retVal != 0)
            {
                Logger.LogError(
                    $"DisconnectEntryAndRemove: Call to RasHangUp returned NON-SUCCESS value of {retVal:X8}");
                disconnectResult = false;
            }
            else
            {
                Logger.LogInformation(
                    "DisconnectEntryAndRemove: Successful return from call to RasHangup. Calling UpdateFiltersState to remove all traffic filters...");

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
            Logger.LogInformation(
                $"Entry '{ActiveConnectionEntryName} is not in a CONNECTED state. Its state is {connectionResult}");
            disconnectResult = true;
        }

        // Now remove all entries in phonebook - for now we only have Guardian Firewall entries
        //RemoveAnyGuardianEntries();
        RemoveAllRasEntries();

        return disconnectResult;
    }

    public static Utility.CheckConnectionResult CheckConnection(string entry_name)
    {
        var waste = HRASCONN.Null;
        return CheckConnection(entry_name, ref waste);
    }

    internal static unsafe Utility.CheckConnectionResult CheckConnection(string entryName, ref HRASCONN handleOut)
    {
        var result = Utility.CheckConnectionResult.DISCONNECTED;
        var status = new RASCONNSTATUSW
        {
            dwSize = (uint)sizeof(RASCONNSTATUSW)
        };

        var connections = GetRasConnections(out var cConnections);
        if (connections.Length == 0)
        {
            Logger.LogError("CheckConnection: GetRasConnections returned empty collection");
            return result;
        }

        for (var i = 0; i < cConnections; i++)
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

    internal static Utility.CheckConnectionResult GetRasConnectStatus(HRASCONN h_ras_conn,
        ref RASCONNSTATUSW lp_ras_status)
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
            var ret = PInvoke.RasEnumEntries(null, null, null, ref entriesBufferSize, out numberOfEntries);
            if (numberOfEntries == 0)
            {
                Logger.LogInformation("RemoveAllRasEntries: No RAS entries found in phonebook.");
                return;
            }

            if (ret != PInvoke.ERROR_BUFFER_TOO_SMALL || entriesBufferSize == 0)
            {
                Logger.LogError(
                    $"RemoveAllRasEntries: Unexpected return value from RasEnumEntries: {ret}, buffer size: {entriesBufferSize}");
                return;
            }

            var entries = new RASENTRYNAMEW[numberOfEntries];
            fixed (RASENTRYNAMEW* pEntries = entries)
            {
                for (var i = 0; i < numberOfEntries; i++) pEntries[i].dwSize = (uint)Marshal.SizeOf<RASENTRYNAMEW>();

                ret = PInvoke.RasEnumEntries(null, null, pEntries, ref entriesBufferSize, out numberOfEntries);
                if (ret != 0)
                {
                    Logger.LogError($"RemoveAllRasEntries: RasEnumEntries failed with error {ret}");
                    return;
                }

                for (var i = 0; i < numberOfEntries; i++)
                {
                    var entryName = pEntries[i].szEntryName.ToString();
                    Logger.LogInformation($"RemoveAllRasEntries: Deleting entry '{entryName}'...");
                    var deleteRet = PInvoke.RasDeleteEntry(null, entryName);
                    if (deleteRet != 0)
                        Logger.LogError(
                            $"RemoveAllRasEntries: RasDeleteEntry failed for '{entryName}' with error {deleteRet}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"RemoveAllRasEntries: Exception thrown: {ex.Message}");
        }
    }
}