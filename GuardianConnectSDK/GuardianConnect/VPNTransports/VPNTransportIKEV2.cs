using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using Windows.Win32.NetworkManagement.Rras;
using GuardianConnect.Abstractions;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Win32Calls;

namespace GuardianConnect.VPNTransports;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class VPNTransportIKEV2 : ITransportProvider
{
    public delegate void PowerEventHandlerCallback();

    private static readonly bool shuttingDown = false;
    private static string? ActiveEntryName;

    public static EventWaitHandle? H_VPNStateChangeServiceEvent;
    public static VPNCallParameters VpnResumeParameters = new();

    public static PowerEventHandlerCallback PowerResumeActions = () => { };
    public static PowerEventHandlerCallback SetVPNStateAtSuspend = () => { };
    public static PowerEventHandlerCallback ResetVPNStateAtSuspend = () => { };

    private static ILogger _logger = NullLogger.Instance;
    private readonly DateTime _connectedDate = DateTime.MinValue;
    private readonly ITransportProvider.VPNConnectionError _lastVpnError = 0;

    private readonly GRDTransportProtocol.TransportProtocol _protocolType =
        GRDTransportProtocol.TransportProtocol.TransportIKEv2;

    private Task? PollingTask;
    private ITransportProvider.VPNProviderStatus _vpnStatus;

    public static ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance) _logger = StaticLoggerFactory.CreateLogger("VPNTransportIKEV2");

            return _logger;
        }
    }


    public virtual GRDTransportProtocol.TransportProtocol ProtocolType => _protocolType;

    public virtual ITransportProvider.VPNProviderStatus VPNStatus => _vpnStatus;

    public virtual ITransportProvider.VPNConnectionError LastVPNError => _lastVpnError;

    public virtual DateTime ConnectedDate => _connectedDate;

    public virtual Task<(ErrorResponse, bool)> StartVPNTunnelAndReturnError()
    {
        throw new NotImplementedException();
    }

    public virtual ErrorResponse DisconnectVPNTunnel()
    {
        var errorResponse = StopVPNTunnel();
        return errorResponse;
    }

    public virtual async Task<ErrorResponse> StartVPNTunnelWithOptions(VPNCallParameters options)
    {
        Logger.LogInformation("VPNTransportIKEV2.StartVPNTunnelWithOptions(): Entry...");
        VpnResumeParameters = options;

        Logger.LogInformation("StartVPNTunnelWithOptions: Evaluating vpn connection parameters...");
        Logger.LogInformation($"EapuserName: {options.EapuserName}");
        Logger.LogInformation($"Eappassword: {options.Eappassword}");
        Logger.LogInformation($"EntryNam: {options.EntryName}");
        Logger.LogInformation($"VpnHostName: {options.VpnHostName}");
        Logger.LogInformation($"VpnHostDisplay: {options.VpnHostDisplay}");

        var creds = new NetworkCredential();

        creds.UserName = options.EapuserName;
        creds.Password = options.Eappassword;

        var entryName = options.EntryName;
        var hostName = options.VpnHostName;
        var hostDisplayName = options.VpnHostDisplay;

        // :CALL POINT:
        var result = ConnectionRoutines.CreateOrUpdateEntry(entryName, hostName, creds.UserName, creds.Password);

        if (result.IsError) return result;

        var connectionCallResult = ConnectToVpnLongRunning(entryName, creds.UserName, creds.Password);

        if (connectionCallResult.IsError) return connectionCallResult;

        NotificationHandler.WasDisconnectPlanned = false;
        Logger.LogInformation(
            $"StartVPNTunnelWithOptions: WasDisconnectPlanned now equals {NotificationHandler.WasDisconnectPlanned}");
        SetVPNStateAtSuspend(); // CHECK - moving to here - makes sense after non-error Connect command return
        Logger.LogInformation(
            $"StartVPNTunnelWithOptions: (CHECK#2) WasDisconnectPlanned now equals {NotificationHandler.WasDisconnectPlanned}");

        // Save off the calling parameters in case we reboot while connected
        var vpnResumeParameters =
            JsonSerializer.Serialize(VpnResumeParameters, VPNCallParametersJsonContext.Default.VPNCallParameters);
        RegistrySettings.UpdateGuardianUserSettings(Common.kVpnCallParametersForReboot, vpnResumeParameters);

        ActiveEntryName = entryName;

        return new ErrorResponse();
    }

    // Called from the ClientPipe Service when a Disconnect command is received
    public virtual ErrorResponse StopVPNTunnel(bool wasDisconnectPlanned = true)
    {
        Logger.LogInformation(
            $"VPNTransportIKEV2.StopVPNTunnel(): Disconnecting entry '{ConnectionRoutines.ActiveConnectionEntryName}' ...");
        NotificationHandler.WasDisconnectPlanned = wasDisconnectPlanned;
        Logger.LogInformation(
            $"StopVPNTunnel: WasDisconnectPlanned now equals {NotificationHandler.WasDisconnectPlanned}");
        try
        {
            if (wasDisconnectPlanned) ResetVPNStateAtSuspend();

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

    public static ITransportProvider.VPNProviderStatus GetCurrentVPNState()
    {
        var status = ITransportProvider.VPNProviderStatus.VPNStatusDisconnected;
        var activeEntryName = string.Empty;
        if (ConnectionRoutines.IsAnyConnectionActive(out activeEntryName))
            status = ITransportProvider.VPNProviderStatus.VPNStatusConnected;

        return status;
    }

    public static void PowerSuspendVPNConnection(bool wasDisconectPlanned = true)
    {
        var vpnTransportIkev2 = new VPNTransportIKEV2();
        vpnTransportIkev2.StopVPNTunnel(wasDisconectPlanned);
    }

    public static ErrorResponse PowerResumeVPNConnection()
    {
        Logger.LogInformation("*************** PowerResumeVPNConnection **************** - Entry...");
        var vpnTransportIkev2 = new VPNTransportIKEV2();
#if true
        Logger.LogInformation(
            "*************** PowerResumeVPNConnection **************** - Calling StartVPNTunnelWithOptions to re-establish connection...");
        var result = vpnTransportIkev2.StartVPNTunnelWithOptions(VpnResumeParameters).Result;
#else
        var userName = VpnResumeParameters.EapuserName;
        var password = VpnResumeParameters.Eappassword;

        var entryName = VpnResumeParameters.EntryName;
        Logger.LogInformation(
            "*************** PowerResumeVPNConnection **************** - Calling ConnectToVPNLongRunning to re-establish connection...");
        var result = vpnTransportIkev2.ConnectToVpnLongRunning(entryName, userName, password);
#endif

        return result;
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
                return new ErrorResponse { Message = "VPN Connection Successful!" };
            }

            return new ErrorResponse
            {
                Data = rasDialRetVal.ToString(),
                IsError = true,
                Message =
                    $"An error occurred when making RASDial VPN Connection call. Return value is  {rasDialRetVal}"
            };
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
            Logger.LogError("ERROR opening H_VPNStateChangeServiceEvent");
            throw new Exception("VPNConnectionEvent WaitHandle Open Exception");
        }

        while (!shuttingDown && !stoppingToken.IsCancellationRequested)
        {
            Logger.LogInformation("PollConnectionState(): Waiting on state change...");

            H_VPNStateChangeServiceEvent?.WaitOne(-1);
            H_VPNStateChangeServiceEvent?.Reset();

            Logger.LogInformation("PollConnectionState(): woke from ConnStateChange.");

            var connectionResult = Utility.CheckConnectionResult.Uninitialized;
            var rasConnStatus = new RASCONNSTATUSW
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
#if true
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