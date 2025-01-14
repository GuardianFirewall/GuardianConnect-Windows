using System.Diagnostics;
using System.Net;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using NativeRoutines;

namespace GuardianConnect.VPNTransports;

public class VPNTransportIKEV2 :ITransportProvider
{
    private static bool shuttingDown = false;
    private static string? ActiveEntryName;
    private ITransportProvider.TransportProtocol _protocolType = ITransportProvider.TransportProtocol.TransportIKEv2;
    private ITransportProvider.VPNProviderStatus _vpnStatus;
    private ITransportProvider.VPNConnectionError _lastVpnError = 0;
    private DateTime _connectedDate = DateTime.MinValue;
    private Task? PollingTask;


    public static Serilog.ILogger? Logger;

    public VPNTransportIKEV2()
    {
        Logger = Common.Logger;
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

    public virtual Task<ErrorResponse> StartVPNTunnelWithOptions(Dictionary<string, object> options)
    {
        Task t = new Task<ErrorResponse>(() =>

        {
            Logger?.Information("StartVPNTunnelWithOptions: Evaluating vpn connection parameters...");
            foreach (string key in options.Keys)
            {
                Logger?.Information($"Key: '{key}': Value='{(string)options[key]}'");
            }

            //_dialer.StateChanged += DialerOnStateChanged;
            NetworkCredential creds = new NetworkCredential();

            creds.UserName = (string)options["eapUser"];
            creds.Password = (string)options["eapPassword"];

            string entryName = (string)options["PhonebookEntryName"];
            string hostName = (string)options["hostName"];
            string hostDisplayName = (string)options["hostDisplay"];

            // :CALL POINT:
            CreateRoutines creator = new NativeRoutines.CreateRoutines();
            var result = creator.CreateTheCall(null, entryName, hostName, creds.UserName, creds.Password);

            if (result != 0)
            {
                return new ErrorResponse("CreateEntry", null, true).SetData(result.ToString());
            }

            // TJE - TODO: Add proper error reporting, bubble-up/handling
            ConnectToVpnLongRunning(entryName, creds.UserName, creds.Password);

            ActiveEntryName = entryName;

            return new ErrorResponse();
        });
        t.Start();
        return Task.FromResult(new ErrorResponse());
    }

    // Called from the IService WCF entry point
    public virtual void StopVPNTunnel(string entryName)
    {
        Logger?.Information($"VPNTransportIKEV2.StopVPNTunnel(): Disconnecting entry '{entryName}' ...");
        ConnectionRoutines.DisconnectEntry(entryName);
    }

    public virtual ErrorResponse FetchLastDisonnectError()
    {
        throw new NotImplementedException();
    }

    public void ConnectToVpnLongRunning(string entryName, string tempUser, string tempPassword)
    {
        var t = new Task(() =>
        {
            Logger?.Information("VPNTransportIKEV2.ConnectoToVpnLongRunning(): Connecting...");
            ConnectionRoutines.MakeTheCall(null, entryName);
            NotificationHandling.StartConnectionStateWatcher();
        });
        t.Start();
    }

    public void StartMonitoringTask()
    {
        PollingTask = Task.Factory.StartNew(PollConnectionState);
    }

    private void PollConnectionState()
    {
        while (!shuttingDown)
        {
            Debug.Write($"[{DateTime.Now:MM/dd/yyyy hh:mm:ss tt]} Polling Task active. ");
            Logger?.Information("VPNTransportIKEV2.PollConnectionState(): Waiting on state change...");
            var succeeded = EventWaitHandle.TryOpenExisting(Common.VPNSTATECHANGE_EVT_NAME, out EventWaitHandle? VPNStateChangeEventHandle);
            if (!succeeded)
            {
                Logger?.Debug( $"ERROR opening VPNStateChangeEventHandle");
                throw new Exception("VPNConnectionEvent WaitHandle Open Exception");
            }

            VPNStateChangeEventHandle?.WaitOne(-1);
            Logger?.Debug($"UI.GCP.Initialize(): woke from ConnStateChange.");
            var cs = NotificationHandling.GetConnectionState();
            var cs2 = GetCurrentVPNState();
            Logger?.Information($"PollConnectionState: cs = {cs}. cs2 = {cs2}");
            switch (cs)
            {
                case Utility.CheckConnectionResult.CONNECTED:
                    _vpnStatus = ITransportProvider.VPNProviderStatus.VPNStatusConnected;
                    break;
                case Utility.CheckConnectionResult.CONNECTING:
                    _vpnStatus = ITransportProvider.VPNProviderStatus.VPNStatusConnecting;
                    break;
                case Utility.CheckConnectionResult.DISCONNECTED:
                    _vpnStatus = ITransportProvider.VPNProviderStatus.VPNStatusDisconnected;
                    break;
                case Utility.CheckConnectionResult.DISCONNECTING:
                    _vpnStatus = ITransportProvider.VPNProviderStatus.VPNStatusDisconnecting;
                    break;
            }
            Debug.WriteLine($" VPN status is {0}...", cs);

            Thread.Sleep(1000);
        }
    }
}
