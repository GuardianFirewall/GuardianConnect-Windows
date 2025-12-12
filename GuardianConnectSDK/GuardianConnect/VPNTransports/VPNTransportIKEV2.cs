using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using Windows.Win32.NetworkManagement.Rras;
using GuardianConnect.Credentials;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using GuardianConnect.Abstractions;
using GuardianConnect.Shared.Extensions;
using Win32Calls;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GuardianConnect.VPNTransports;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class VPNTransportIKEV2 : ITransportProvider
{
    private static bool shuttingDown = false;
    private static string? ActiveEntryName;
    private ITransportProvider.TransportProtocol _protocolType = ITransportProvider.TransportProtocol.TransportIKEv2;
    private ITransportProvider.VPNProviderStatus _vpnStatus;
    private ITransportProvider.VPNConnectionError _lastVpnError = 0;
    private DateTime _connectedDate = DateTime.MinValue;
    private Task? PollingTask;

    public static EventWaitHandle? H_VPNStateChangeServiceEvent;
    public static VPNCallParameters VpnResumeParameters = new VPNCallParameters();

    public delegate void PowerEventHandlerCallback();

    public static PowerEventHandlerCallback PowerResumeActions = () => { };
    public static PowerEventHandlerCallback SetVPNStateAtSuspend = () => { };
    public static PowerEventHandlerCallback ResetVPNStateAtSuspend = () => { };

    private static Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;

    public static Microsoft.Extensions.Logging.ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance)
            {
                _logger = StaticLoggerFactory.CreateLogger("VPNTransportIKEV2");
            }

            return _logger;
        }
    }


    public virtual ITransportProvider.TransportProtocol ProtocolType => _protocolType;

    public virtual ITransportProvider.VPNProviderStatus VPNStatus => _vpnStatus;

    public virtual ITransportProvider.VPNConnectionError LastVPNError => _lastVpnError;

    public virtual DateTime ConnectedDate => _connectedDate;

    // TJE - revisit this - I don't like scattered and seemingly redundant data and methods
    public static ITransportProvider.VPNProviderStatus GetCurrentVPNState()
    {
        ITransportProvider.VPNProviderStatus status = ITransportProvider.VPNProviderStatus.VPNStatusDisconnected;
        String activeEntryName = String.Empty;
        if (ConnectionRoutines.IsAnyConnectionActive(out activeEntryName))
        {
            status = ITransportProvider.VPNProviderStatus.VPNStatusConnected;
        }

        return status;
    }

    public virtual Task<(ErrorResponse, bool)> StartVPNTunnelAndReturnError()
    {
        throw new NotImplementedException();
    }

    public virtual ErrorResponse DisconnectVPNTunnel()
    {
        var errorResponse = StopVPNTunnel();
        return errorResponse;
    }

    public static void PowerSuspendVPNConnection()
    {
        string entryName = VpnResumeParameters.EntryName;
        var vpnTransportIkev2 = new VPNTransportIKEV2();
        vpnTransportIkev2.StopVPNTunnel();
    }

    public static ErrorResponse PowerResumeVPNConnection()
    {
        Logger.LogInformation("*************** PowerResumeVPNConnection **************** - Entry...");
        var vpnTransportIkev2 = new VPNTransportIKEV2();
// TJE - don't do this - we already have phonebook entry created. Just MakeTheCall
//        var result = vpnTransportIkev2.StartVPNTunnelWithOptions(VpnResumeParameters).Result;
        var userName = VpnResumeParameters.EapuserName;
        var password = VpnResumeParameters.Eappassword;

        var entryName = VpnResumeParameters.EntryName;
        Logger.LogInformation(
            "*************** PowerResumeVPNConnection **************** - Calling ConnectToVPNLongRunning to re-establish connection...");
        var result = vpnTransportIkev2.ConnectToVpnLongRunning(entryName, userName, password);

        return result;
    }

    public async virtual Task<ErrorResponse> StartVPNTunnelWithOptions(VPNCallParameters options)
    {
        Logger.LogInformation("VPNTransportIKEV2.StartVPNTunnelWithOptions(): Entry...");
        VpnResumeParameters = options;

        //Task<ErrorResponse> t = new Task<ErrorResponse>(() =>
        //{
            Logger.LogInformation("StartVPNTunnelWithOptions: Evaluating vpn connection parameters...");
            Logger.LogInformation($"EapuserName: {options.EapuserName}");
            Logger.LogInformation($"Eappassword: {options.Eappassword}");
            Logger.LogInformation($"EntryNam: {options.EntryName}");
            Logger.LogInformation($"VpnHostName: {options.VpnHostName}");
            Logger.LogInformation($"VpnHostDisplay: {options.VpnHostDisplay}");

            NetworkCredential creds = new NetworkCredential();

            creds.UserName = options.EapuserName;
            creds.Password = options.Eappassword;

            string entryName = options.EntryName;
            string hostName = options.VpnHostName;
            string hostDisplayName = options.VpnHostDisplay;

            // :CALL POINT:
            var result = ConnectionRoutines.CreateOrUpdateEntry(entryName, hostName, creds.UserName, creds.Password);

            if (result.IsError) return result;

            ErrorResponse connectionCallResult = ConnectToVpnLongRunning(entryName, creds.UserName, creds.Password);

            if (connectionCallResult.IsError) return connectionCallResult;

            NotificationHandler.WasDisconnectPlanned = false;
            Logger.LogInformation(
                $"StartVPNTunnelWithOptions: WasDisconnectPlanned now equals {NotificationHandler.WasDisconnectPlanned}");
            SetVPNStateAtSuspend(); // CHECK THIS - moving to here - makes sense after non-error Connect command return
            Logger.LogInformation(
                $"StartVPNTunnelWithOptions: (CHECK#2) WasDisconnectPlanned now equals {NotificationHandler.WasDisconnectPlanned}");

            // Save off the calling parameters in case we reboot while connected
            var vpnResumeParameters = JsonSerializer.Serialize(VpnResumeParameters,
                VPNCallParametersJsonContext.Default.VPNCallParameters);
            RegistrySettings.UpdateGuardianUserSettings(Common.kVpnCallParametersForReboot, vpnResumeParameters);

            ActiveEntryName = entryName;

            return new ErrorResponse();
        //});
        //t.Start();
        //return t;
    }

    // Called from the ClientPipe Service when a Disconnect command is received
    public virtual ErrorResponse StopVPNTunnel()
    {
        Logger.LogInformation(
            $"VPNTransportIKEV2.StopVPNTunnel(): Disconnecting entry '{ConnectionRoutines.ActiveConnectionEntryName}' ...");
        NotificationHandler.WasDisconnectPlanned = true;
        Logger.LogInformation(
            $"StopVPNTunnel: WasDisconnectPlanned now equals {NotificationHandler.WasDisconnectPlanned}");
        try
        {

            ResetVPNStateAtSuspend(); // CHECK THIS - moving to here - makes sense after non-error Disconnect command return
            ConnectionRoutines.DisconnectEntryAndRemove();
            return new ErrorResponse();
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"VPNTransportIKEV2.StopVPNTunnel(): Exception during Disconnect: {e.Message}");
            return new ErrorResponse
            {
                IsError = true,
                Message = $"Exception during Disconnect: {e.Message}",
                ThrownException = e
            };
        }
    }

    public virtual ErrorResponse FetchLastDisonnectError()
    {
        throw new NotImplementedException();
    }

    public ErrorResponse ConnectToVpnLongRunning(string entryName, string tempUser, string tempPassword)
    {
        var t = new Task<ErrorResponse>(() =>
        {
            Logger.LogInformation("VPNTransportIKEV2.ConnectoToVpnLongRunning(): Connecting...");
            var rasDialRetVal = ConnectionRoutines.ConnectEntry();
            if (!rasDialRetVal
                    .IsError) // no premature errors from bad calling data/conventions or state of network/RRAS subsystem
            {
                NotificationHandler.StartRasConnectStateWatcher();
                return new ErrorResponse() { Message = "VPN Connection Successful!" };
            }
            else
            {
                return new ErrorResponse
                {
                    Data = rasDialRetVal.ToString(),
                    IsError = true,
                    Message =
                        $"An error occurred when making RASDial VPN Connection call. Return value is  {rasDialRetVal}"
                };
            }

            return new ErrorResponse();
        });
        t.Start();

        return t.Result;
    }

    public void StartMonitoringTask(CancellationToken stoppingToken)
    {
        PollingTask = Task.Factory.StartNew(() => PollConnectionState(stoppingToken), stoppingToken);
    }

    private unsafe void PollConnectionState(CancellationToken stoppingToken)
    {
        var succeeded = EventWaitHandle.TryOpenExisting(Common.VPNEVT_NAME_SVRSIDE, out H_VPNStateChangeServiceEvent);
        if (!succeeded)
        {
            Logger.LogError($"ERROR opening H_VPNStateChangeServiceEvent");
            throw new Exception("VPNConnectionEvent WaitHandle Open Exception");
        }

        while (!shuttingDown && !stoppingToken.IsCancellationRequested)
        {
            Logger.LogInformation("PollConnectionState(): Waiting on state change...");

            H_VPNStateChangeServiceEvent?.WaitOne(-1);
            H_VPNStateChangeServiceEvent?.Reset();

            Logger.LogInformation($"PollConnectionState(): woke from ConnStateChange.");
            // TJE TODO: change # of clients connected to be available here so we can set signal only if clients connected
            //Logger.LogInformation($"PollConnectionState(): Clients connected - signalling them.");

            // Logger.LogInformation($"PollConnectionState(): Signaling clients ...");

            Utility.CheckConnectionResult connectionResult = Utility.CheckConnectionResult.Uninitialized;
            RASCONNSTATUSW rasConnStatus = new RASCONNSTATUSW
            {
                dwSize = (uint)sizeof(RASCONNSTATUSW)
            };

            var cs = ITransportProvider.VPNProviderStatus.VPNStatusInvalid;
            try
            {

                Logger.LogInformation(
                    "PollConnectionState(): Calling ConnectionRoutines.GetConnectionState to get current status...");
                connectionResult =
                    ConnectionRoutines.GetRasConnectStatus(ConnectionRoutines.ActiveConnectionHandle,
                        ref rasConnStatus);

                Logger.LogInformation("PollConnectionState(): Calling GetCurrentVPNState() to get current status...");
                cs = GetCurrentVPNState();
                Logger.LogInformation($"PollConnectionState: [GetCurrentVPNState] = {cs}.");
                Logger.LogInformation(
                    $"PollConnectionState: [RasConnStatusInfo.RasConState] = {rasConnStatus.rasconnstate}.");
                Logger.LogInformation(
                    $"PollConnectionState: [RasConnStatusInfo.RasConSubState] = {rasConnStatus.rasconnsubstate}.");
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"PollConnectionState: Exception thrown for some reason: {e.Message}");
            }

            switch (cs)
            {
                case ITransportProvider.VPNProviderStatus.VPNStatusConnected:
                    _vpnStatus = ITransportProvider.VPNProviderStatus.VPNStatusConnected;
                    break;
                case ITransportProvider.VPNProviderStatus.VPNStatusDisconnected:
                    // TEST THIS - check flag if intended disconnect or not (i.e., after sleep)
                    _vpnStatus = ITransportProvider.VPNProviderStatus.VPNStatusDisconnected;
                    if (!NotificationHandler.WasDisconnectPlanned)
                    {
#if NOTREADYYET
                        Logger.LogInformation(
                            "PollConnectionState: ****************** UNPLANNED DISCONNECT. Setting VPNStateAtSuspend to CONNECTED for when resuming...");
                        SetVPNStateAtSuspend();
#else
                        Logger.LogInformation( "PollConnectionState: ****************** UNPLANNED DISCONNECT. IGNORING for now...");
#endif
                    }

                    break;
                default:
                    Logger.LogWarning($"PollConnectionState: !!!!!!!!!!!!!!! UNHANDLED CS VALUE: {cs}");
                    break;
            }

            //Thread.Sleep(1000);
        }
    }
}
