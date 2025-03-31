using System.IO.Pipes;
using GuardianConnect.Shared;
using Newtonsoft.Json;
using Serilog;

namespace GuardianConnect.Helpers;

public static class ClientPipe
{
    private static readonly ClientPipeImpl Instance = new ClientPipeImpl();

    public static void Connect(string servicePipeName = Common.kGRDServicePipeName)
    {
        Instance.Connect(servicePipeName);
    }

    public static IGuardianNPContract.CompositeType GetDataUsingDataContract(IGuardianNPContract.CompositeType composite)
    {
        return Instance.GetDataUsingDataContract(composite);
    }

    public static bool StartVPNConnection(Dictionary<string, object> protocolRequest)
    {
        return Instance.StartVPNConnection(protocolRequest);
    }

    public static void DisconnectVPNConnection(string entryName)
    {
        Instance.DisconnectVPNConnection(entryName);
    }

    public static IGuardianNPContract.CurrentVPNStatus GetCurrentVpnConnectionStatus()
    {
        return Instance.GetCurrentVpnConnectionStatus();
    }

    public static async Task<string> Ping()
    {
        return await Instance.Ping();
    }

    public static void ToggleLogging(bool whetherToDeleteLogFiles)
    {
        Instance.ToggleLogging(whetherToDeleteLogFiles);

    }

    public static async Task<List<string>> GetServiceLogLinesAsync(int maxNumberOfLinesToGet = 200)
    {
        return await Instance.GetServiceLogLinesAsync(maxNumberOfLinesToGet);
    }
}

public class ClientPipeImpl : IGuardianNPContract
{
    private static NamedPipeClientStream _clientStream = new NamedPipeClientStream("NULL");
    private static StreamString ss;

    internal void Connect(string servicePipeName = Common.kGRDServicePipeName)
    {
        try
        {
            _clientStream = new NamedPipeClientStream(".", servicePipeName, PipeDirection.InOut);
            _clientStream.Connect(10 * 1000);
            ss = new StreamString(_clientStream);
            var testAck = ss.ReadStringAsync();
            Log.Information($"Client Pipe connected to Service - received '{testAck}'");
        }
        catch (Exception e)
        {
            Log.Error(e, $"Exception when Connecting on pipe: {e.Message}");
            throw;
        }
    }

    string IGuardianNPContract.GetData(int value)
    {
        throw new NotImplementedException();
    }

    public IGuardianNPContract.CompositeType GetDataUsingDataContract(IGuardianNPContract.CompositeType composite)
    {
        var cmdPayload = JsonConvert.SerializeObject(composite);
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.GetDataUsingDataContract}.{cmdPayload}";
        ss.WriteString(cmdString);
        var response = ss.ReadStringAsync().Result;
        var value = JsonConvert.DeserializeObject<IGuardianNPContract.CompositeType>(response);

        if (value == null)
        {
            throw new InvalidOperationException("Service returned null");
        }

        return value;
    }

    public bool StartVPNConnection(Dictionary<string, object> protocolRequest)
    {
        var cmdPayload = JsonConvert.SerializeObject(protocolRequest);
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.StartVPNConnection}.{cmdPayload}";
        ss.WriteString(cmdString);
        var started = ss.ReadStringAsync().Result;

        return started.Equals("True");
    }

    public void DisconnectVPNConnection(string entryName)
    {
        var cmdPayload = JsonConvert.SerializeObject(entryName);
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.DisconnectVPNConnection}.{cmdPayload}";
        ss.WriteString(cmdString);
    }

    public IGuardianNPContract.CurrentVPNStatus GetCurrentVpnConnectionStatus()
    {
        Log.Information("Calling service to GetCurrentVpnConnectionStatus...");
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.GetCurrentVpnConnectionStatus}.";
        ss.WriteString(cmdString);
        Log.Information("Reading status...");
        //var statusString = ss.ReadStringAsync().Result;
        var statusString = ss.ReadString();
        var status = JsonConvert.DeserializeObject<IGuardianNPContract.CurrentVPNStatus>(statusString);
        Log.Information($"status is {status.EntryName}, {status.ConnectionState}...");

        return status;
    }

    public async Task<string> Ping()
    {
        Log.Information("Pinging service");
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.Ping}.";
        ss.WriteString(cmdString);
        Log.Information("Reading status...");
        var ping = await ss.ReadStringAsync();
        Log.Information($"Service returned {ping}");
        return ping;
    }

    public void ShutdownService()
    {
        throw new NotImplementedException();
    }

    public void ToggleLogging(bool whetherToDeleteLogFiles)
    {
        var msg = Common.LogFilterOn ? "OFF" : "ON";
        Log.Information($"Telling Service to turn Logging {msg}");
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.ToggleLogging}.{whetherToDeleteLogFiles.ToString()}";
        ss.WriteString(cmdString);
    }

    public async Task<List<string>> GetServiceLogLinesAsync(int maxNumberOfLinesToGet = 200)
    {
        Log.Information($"Requesting GuardianFirewall Service's last {maxNumberOfLinesToGet} log lines...");
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.RequestLogLines}.{maxNumberOfLinesToGet}";
        ss.WriteString(cmdString);
        Log.Information("Reading response...");
        var serializedServiceLogLines = await ss.ReadStringAsync();
        var jsonLines = JsonConvert.DeserializeObject<List<string>>(serializedServiceLogLines);
        var serviceLogLines = jsonLines ?? new List<string>();
        Log.Information($"Number of log lines returned from the service = {serviceLogLines.Count}");

        return serviceLogLines;
    }
}
