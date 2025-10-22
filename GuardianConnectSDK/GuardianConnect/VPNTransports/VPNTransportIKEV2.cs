using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using Windows.Win32.NetworkManagement.Rras;
using GuardianConnect.Credentials;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using Win32Calls;
using System.Text.Json.Serialization;
using Serilog;

namespace GuardianConnect.VPNTransports;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
public class VPNTransportIKEV2 :ITransportProvider
{
    private static bool shuttingDown = false;
    private static string? ActiveEntryName;
    private ITransportProvider.TransportProtocol _protocolType = ITransportProvider.TransportProtocol.TransportIKEv2;
    private ITransportProvider.VPNProviderStatus _vpnStatus;
    private ITransportProvider.VPNConnectionError _lastVpnError = 0;
    private DateTime _connectedDate = DateTime.MinValue;
    private Task? PollingTask;

   public static VPNCallParameters VpnResumeParameters = new VPNCallParameters();
   public delegate void PowerEventHandlerCallback();
   public static PowerEventHandlerCallback PowerResumeActions = () => { };
   public static PowerEventHandlerCallback SetVPNStateAtSuspend = () => { };
   public static PowerEventHandlerCallback ResetVPNStateAtSuspend = () => { };

    public VPNTransportIKEV2()
    {
        Log.Information("VPNTransportIKEV2 logger!");
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

    public virtual async Task<ErrorResponse> DisconnectVPNTunnel()
    {
        await new Task(() =>
        {
            StopVPNTunnel();
        });
        return new ErrorResponse();
    }

    public static void PowerSuspendVPNConnection()
    {
        string entryName = VpnResumeParameters.EntryName;
        var vpnTransportIkev2 = new VPNTransportIKEV2();
        vpnTransportIkev2.StopVPNTunnel();
    }
    
    public static ErrorResponse PowerResumeVPNConnection()
    {
        Log.Information("*************** PowerResumeVPNConnection **************** - Entry...");
        var vpnTransportIkev2 = new VPNTransportIKEV2();
// TJE - don't do this - we already have phonebook entry created. Just MakeTheCall
//        var result = vpnTransportIkev2.StartVPNTunnelWithOptions(VpnResumeParameters).Result;
        var userName = VpnResumeParameters.EapuserName;
        var password = VpnResumeParameters.Eappassword;

        var entryName = VpnResumeParameters.EntryName;
        Log.Information("*************** PowerResumeVPNConnection **************** - Calling ConnectToVPNLongRunning to re-establish connection...");
        var result = vpnTransportIkev2.ConnectToVpnLongRunning(entryName, userName, password);

        return result;
    }
    
    public virtual Task<ErrorResponse> StartVPNTunnelWithOptions(VPNCallParameters options)
    {
        VpnResumeParameters = options;
        
        Task<ErrorResponse> t = new Task<ErrorResponse>(() =>
        {
            Log.Information("StartVPNTunnelWithOptions: Evaluating vpn connection parameters...");
            Log.Information($"EapuserName: {options.EapuserName}");
            Log.Information($"Eappassword: {options.Eappassword}");
            Log.Information($"EntryNam: {options.EntryName}");
            Log.Information($"VpnHostName: {options.VpnHostName}");
            Log.Information($"VpnHostDisplay: {options.VpnHostDisplay}");

            NetworkCredential creds = new NetworkCredential();

            creds.UserName = options.EapuserName;
            creds.Password = options.Eappassword;

            string entryName = options.EntryName;
            string hostName = options.VpnHostName;
            string hostDisplayName = options.VpnHostDisplay;

            // :CALL POINT:
            var result = ConnectionRoutines.CreateOrUpdateEntry(entryName, hostName, creds.UserName, creds.Password);

            if (result.IsError) return result;

            // TJE - TODO: Add proper error reporting, bubble-up/handling
            ErrorResponse connectionCallResult = ConnectToVpnLongRunning(entryName, creds.UserName, creds.Password);

            if (connectionCallResult.IsError) return connectionCallResult;
            
            NotificationHandler.WasDisconnectPlanned = false;
            Log.Information($"StartVPNTunnelWithOptions: WasDisconnectPlanned now equals {NotificationHandler.WasDisconnectPlanned}");
            SetVPNStateAtSuspend(); // CHECK THIS - moving to here - makes sense after non-error Connect command return
            Log.Information($"StartVPNTunnelWithOptions: (CHECK#2) WasDisconnectPlanned now equals {NotificationHandler.WasDisconnectPlanned}");
            
            // Save off the calling parameters in case we reboot while connected
            var vpnResumeParameters = JsonSerializer.Serialize(VpnResumeParameters, VPNCallParametersJsonContext.Default.VPNCallParameters);
            RegistrySettings.UpdateGuardianUserSettings(Common.kVpnCallParametersForReboot, vpnResumeParameters);

            ActiveEntryName = entryName;

            return new ErrorResponse();
        });
        t.Start();
        return t;
    }

    // Called from the ClientPipe Service when a Disconnect command is received
    public virtual void StopVPNTunnel()
    {
        Log.Information($"VPNTransportIKEV2.StopVPNTunnel(): Disconnecting entry '{ConnectionRoutines.ActiveConnectionEntryName}' ...");
        NotificationHandler.WasDisconnectPlanned = true;
        Log.Information($"StopVPNTunnel: WasDisconnectPlanned now equals {NotificationHandler.WasDisconnectPlanned}");
        ResetVPNStateAtSuspend(); // CHECK THIS - moving to here - makes sense after non-error Disconnect command return
        ConnectionRoutines.DisconnectEntry();
    }

    public virtual ErrorResponse FetchLastDisonnectError()
    {
        throw new NotImplementedException();
    }

    public ErrorResponse ConnectToVpnLongRunning(string entryName, string tempUser, string tempPassword)
    {
        var t = new Task<ErrorResponse>(() =>
        {
            Log.Information("VPNTransportIKEV2.ConnectoToVpnLongRunning(): Connecting...");
            var rasDialRetVal = ConnectionRoutines.ConnectEntry();
            if (!rasDialRetVal.IsError ) // no premature errors from bad calling data/conventions or state of network/RRAS subsystem
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

    public void StartMonitoringTask()
    {
        PollingTask = Task.Factory.StartNew(PollConnectionState);
    }

    private unsafe void PollConnectionState()
    {
        var succeeded = EventWaitHandle.TryOpenExisting(Common.VPNEVT_NAME_SVRSIDE, out EventWaitHandle? H_VPNStateChangeServiceEvent);
        if (!succeeded)
        {
            Log.Error( $"ERROR opening H_VPNStateChangeServiceEvent");
            throw new Exception("VPNConnectionEvent WaitHandle Open Exception");
        }
        
        while (!shuttingDown)
        {
            Log.Information("PollConnectionState(): Waiting on state change...");

            H_VPNStateChangeServiceEvent?.WaitOne(-1);
            H_VPNStateChangeServiceEvent?.Reset();
            
            Log.Information($"PollConnectionState(): woke from ConnStateChange.");
            // TJE TODO: change # of clients connected to be available here so we can set signal only if clients connected
            //Log.Information($"PollConnectionState(): Clients connected - signalling them.");
            
           // Log.Information($"PollConnectionState(): Signaling clients ...");

           Utility.CheckConnectionResult connectionResult = Utility.CheckConnectionResult.Uninitialized;
           RASCONNSTATUSW rasConnStatus = new RASCONNSTATUSW
           {
               dwSize = (uint)sizeof(RASCONNSTATUSW)
           };

           var cs = ITransportProvider.VPNProviderStatus.VPNStatusInvalid;
           try
           {

               Log.Information( "PollConnectionState(): Calling ConnectionRoutines.GetConnectionState to get current status...");
               connectionResult = ConnectionRoutines.GetRasConnectStatus(ConnectionRoutines.ActiveConnectionHandle, ref rasConnStatus);

               Log.Information("PollConnectionState(): Calling GetCurrentVPNState() to get current status...");
               cs = GetCurrentVPNState();
               Log.Information($"PollConnectionState: [GetCurrentVPNState] = {cs}.");
               Log.Information( $"PollConnectionState: [RasConnStatusInfo.RasConState] = {rasConnStatus.rasconnstate}.");
               Log.Information( $"PollConnectionState: [RasConnStatusInfo.RasConSubState] = {rasConnStatus.rasconnsubstate}.");
           }
           catch (Exception e)
           {
               Log.Error(e, $"PollConnectionState: Exception thrown for some reason: {e.Message}");
           }

           switch (cs)
            {
                //case Utility.CheckConnectionResult.CONNECTED:
                //case ConnectionRoutines.RasConnState.Connected:
                //case 8192: // Connected
                case ITransportProvider.VPNProviderStatus.VPNStatusConnected:
                    _vpnStatus = ITransportProvider.VPNProviderStatus.VPNStatusConnected;
                    break;
                /*
                //case Utility.CheckConnectionResult.CONNECTING:
                case ConnectionRoutines.RasConnState.Connecting:
                    _vpnStatus = ITransportProvider.VPNProviderStatus.VPNStatusConnecting;
                    break;
                */
                //case Utility.CheckConnectionResult.DISCONNECTED:
                //case ConnectionRoutines.RasConnState.Disconnected:
                //case 8193: // Disconnected
                case ITransportProvider.VPNProviderStatus.VPNStatusDisconnected:
                    // TEST THIS - check flag if intended disconnect or not (i.e., after sleep)
                    _vpnStatus = ITransportProvider.VPNProviderStatus.VPNStatusDisconnected;
                    if (!NotificationHandler.WasDisconnectPlanned)
                    {
                        Log.Information( "PollConnectionState: ****************** UNPLANNED DISCONNECT. Setting VPNStateAtSuspend to CONNECTED for when resuming...");
                        //PowerResumeVPNConnection();
                        //PowerResumeActions(); /* This is delegate into PowerHandler in PowerTransitionHandler
                        SetVPNStateAtSuspend();
                    }
                    break;
                /*
//                case Utility.CheckConnectionResult.DISCONNECTING:
//                    _vpnStatus = ITransportProvider.VPNProviderStatus.VPNStatusDisconnecting;
//                    break;
                    */
                default:
                    Log.Warning($"PollConnectionState: !!!!!!!!!!!!!!! UNHANDLED CS VALUE: {cs}");
                    break;
            }

            //Thread.Sleep(1000);
        }
    }
}
