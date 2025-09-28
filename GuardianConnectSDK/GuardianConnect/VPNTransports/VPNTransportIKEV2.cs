using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
//using ABI.Windows.Data.Json;
using GuardianConnect.Credentials;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using NativeRoutines;
using Newtonsoft.Json;
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
    
   public static Dictionary<string, object> VpnResumeParameters = new Dictionary<string, object>();
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
        unsafe
        {
            char* aen = null;
            if (ConnectionRoutines.IsAnyConnectionActive(aen))
            {
                status = ITransportProvider.VPNProviderStatus.VPNStatusConnected;
            }
        }

        return status;
    }

    public virtual Task<(ErrorResponse, bool)> StartVPNTunnelAndReturnError()
    {
        throw new NotImplementedException();
    }

    public virtual async Task<ErrorResponse> DisconnectVPNTunnel(string entryName)
    {
        await new Task(() =>
        {
            StopVPNTunnel(entryName);
        });
        return new ErrorResponse();
    }

    public static void PowerSuspendVPNConnection()
    {
        string entryName = (string)VpnResumeParameters["PhonebookEntryName"];
        var vpnTransportIkev2 = new VPNTransportIKEV2();
        vpnTransportIkev2.StopVPNTunnel(entryName);
    }
    
    public static ErrorResponse PowerResumeVPNConnection()
    {
        Log.Information("*************** PowerResumeVPNConnection **************** - Entry...");
        var vpnTransportIkev2 = new VPNTransportIKEV2();
// TJE - don't do this - we already have phonebook entry created. Just MakeTheCall
//        var result = vpnTransportIkev2.StartVPNTunnelWithOptions(VpnResumeParameters).Result;
        var userName = (string)VpnResumeParameters["eapUser"];
        var password = (string)VpnResumeParameters["eapPassword"];

        var entryName = (string)VpnResumeParameters["PhonebookEntryName"];
        Log.Information("*************** PowerResumeVPNConnection **************** - Calling ConnectToVPNLongRunning to re-establish connection...");
        var result = vpnTransportIkev2.ConnectToVpnLongRunning(entryName, userName, password);

        return result;
    }
    
    public virtual Task<ErrorResponse> StartVPNTunnelWithOptions(Dictionary<string, object> options)
    {
        VpnResumeParameters = options;
        
        Task<ErrorResponse> t = new Task<ErrorResponse>(() =>
        {
            Log.Information("StartVPNTunnelWithOptions: Evaluating vpn connection parameters...");
            foreach (string key in options.Keys)
            {
                Log.Information($"Key: '{key}': Value='{(string)options[key]}'");
            }

            //_dialer.StateChanged += DialerOnStateChanged;
            NetworkCredential creds = new NetworkCredential();

            creds.UserName = (string)options["eapUser"];
            creds.Password = (string)options["eapPassword"];

            string entryName = (string)options["PhonebookEntryName"];
            string hostName = (string)options["hostName"];
            string hostDisplayName = (string)options["hostDisplay"];

            // :CALL POINT:
            CreateRoutines creator = new CreateRoutines();
            var result = creator.CreateTheCall(null, entryName, hostName, creds.UserName, creds.Password);

            if (result != 0)
            {
                return new ErrorResponse("CreateEntry", null, true).SetData(result.ToString());
            }

            // TJE - TODO: Add proper error reporting, bubble-up/handling
            ErrorResponse connectionCallResult = ConnectToVpnLongRunning(entryName, creds.UserName, creds.Password);

            if (connectionCallResult.IsError) return connectionCallResult;
            
            NotificationHandling.WasDisconnectPlanned = false;
            SetVPNStateAtSuspend(); // CHECK THIS - moving to here - makes sense after non-error Connect command return
            
            // Save off the calling parameters in case we reboot while connected
            var vpnResumeParameters = JsonConvert.SerializeObject(VpnResumeParameters);
            RegistrySettings.UpdateGuardianUserSettings(Common.kVpnCallParametersForReboot, vpnResumeParameters);

            ActiveEntryName = entryName;

            return new ErrorResponse();
        });
        t.Start();
        return t;
    }

    // Called from the IService WCF entry point
    public virtual void StopVPNTunnel(string entryName)
    {
        Log.Information($"VPNTransportIKEV2.StopVPNTunnel(): Disconnecting entry '{entryName}' ...");
        NotificationHandling.WasDisconnectPlanned = true;
        ResetVPNStateAtSuspend(); // CHECK THIS - moving to here - makes sense after non-error Disconnect command return
        ConnectionRoutines.DisconnectEntry(entryName);
    }

    public virtual ErrorResponse FetchLastDisonnectError()
    {
        throw new NotImplementedException();
    }

    public ErrorResponse ConnectToVpnLongRunning(string entryName, string tempUser, string tempPassword)
    {
        var t = new Task<ErrorResponse>(() =>
        {
            var errorResult = new ErrorResponse();
            Log.Information("VPNTransportIKEV2.ConnectoToVpnLongRunning(): Connecting...");
            var rasDialRetVal = ConnectionRoutines.MakeTheCall(null, entryName);
            if (rasDialRetVal == 0) // no premature errors from bad calling data/conventions or state of network/RRAS subsystem
            {
                NotificationHandling.StartRasConnectStateWatcher();
                errorResult.Message = "VPN Connection Successful!";
            }
            else
            {
                errorResult.SetData(rasDialRetVal.ToString());
                errorResult.IsError = true;
                errorResult.Message = $"An error occurred when making RASDial VPN Connection call. Return value is  {rasDialRetVal}";
            }
            return errorResult;
        });
        t.Start();

        return t.Result;
    }

    public void StartMonitoringTask()
    {
        PollingTask = Task.Factory.StartNew(PollConnectionState);
    }

    private void PollConnectionState()
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
            
            Utility.CheckConnectionResult connectionResult;
            ConnectionRoutines.RasConnStatusInfo rasConnStatusInfo;
            Log.Information("PollConnectionState(): Calling ConnectionRoutines.GetConnectionState to get current status...");
            unsafe
            {
                nint hRasConn = (nint)ConnectionRoutines.ActiveConnectionHandle;
                connectionResult = ConnectionRoutines.GetConnectionState(hRasConn, out rasConnStatusInfo);
            }
            
            Log.Information("PollConnectionState(): Calling GetCurrentVPNState() to get current status...");
            var cs = GetCurrentVPNState();
            Log.Information($"PollConnectionState: [NoficationHandling.GetConnectionState] = {cs}.");
            Log.Information($"PollConnectionState: [RasConnStatusInfo.RasConState] = {rasConnStatusInfo.RasConnState}.");
            Log.Information($"PollConnectionState: [RasConnStatusInfo.RasConSubState] = {rasConnStatusInfo.RasConnSubState}.");
            Log.Information($"PollConnectionState: [RasConnStatusInfo] = {rasConnStatusInfo}.");
            switch (connectionResult)
            {
                case Utility.CheckConnectionResult.CONNECTED:
                    _vpnStatus = ITransportProvider.VPNProviderStatus.VPNStatusConnected;
                    break;
                /*
                case Utility.CheckConnectionResult.CONNECTING:
                    _vpnStatus = ITransportProvider.VPNProviderStatus.VPNStatusConnecting;
                    break;
                */
                case Utility.CheckConnectionResult.DISCONNECTED:
                    // TEST THIS - check flag if intended disconnect or not (i.e., after sleep)
                    _vpnStatus = ITransportProvider.VPNProviderStatus.VPNStatusDisconnected;
                    if (!NotificationHandling.WasDisconnectPlanned)
                    {
                        Log.Information( "PollConnectionState: ****************** UNPLANNED DISCONNECT. Setting VPNStateAtSuspend to CONNECTED for when resuming...");
                        //PowerResumeVPNConnection();
                        //PowerResumeActions(); /* This is delegate into PowerHandler in PowerTransitionHandler
                        SetVPNStateAtSuspend();
                    }
                    break;
                /*
                case Utility.CheckConnectionResult.DISCONNECTING:
                    _vpnStatus = ITransportProvider.VPNProviderStatus.VPNStatusDisconnecting;
                    break;
                    */
                default:
                    Log.Warning($"PollConnectionState: !!!!!!!!!!!!!!! UNHANDLED CS VALUE: {cs}");
                    break;
            }

            //Thread.Sleep(1000);
        }
    }
}
