using System.ServiceProcess;
using Serilog;

namespace GuardianFirewallService;

public class PowerHandlerService :ServiceBase
{
    internal PowerHandlerService()
    {
        CanHandlePowerEvent = true;
        ServiceName = "GuardianFirewallServicePowerHandler";
        AutoLog = true;
    }
    
    public bool CanHandlePowerEvent { get; set; } = true;

    protected override void OnStart(string[] args)
    {
        Log.Information("Starting PowerHandlerService");
        base.OnStart(args);
    }

    protected override void OnPause()
    {
        Log.Information("PowerHandlerService Paused");
        base.OnPause();
    }

    protected override void OnContinue()
    {
        Log.Information("PowerHandlerService Continued");
        base.OnContinue();
    }

    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        Log.Information($"PowerHandlerService received PowerEvent: {powerStatus}");
        return base.OnPowerEvent(powerStatus);
    }

    protected override void OnSessionChange(SessionChangeDescription changeDescription)
    {
        Log.Information($"PowerHandlerService SessionChange {changeDescription}");
        base.OnSessionChange(changeDescription);
    }

    protected override void OnShutdown()
    {
        Log.Information("PowerHandlerService Shutdown");
        base.OnShutdown();
    }

    protected override void OnStop()
    {
        Log.Information("PowerHandlerService Stopped");
        base.OnStop();
    }
}