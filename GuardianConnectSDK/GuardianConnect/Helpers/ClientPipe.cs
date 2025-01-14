using System.IO.Pipes;
using GuardianConnect.Shared;
using Newtonsoft.Json;
using Serilog;

namespace GuardianConnect.Helpers;

public class ClientPipe : IGuardianNPContract
{
    private IGuardianNPContract.NPCommands cmds;
    private NamedPipeClientStream _clientStream;
    private StreamString ss;
    private static bool _instanceCreated = false;

    private static ClientPipe? _instance;

    public ClientPipe()
    {
    }
    
    public static ClientPipe Instance
    {
        get
        {
            if (_instance == null)
            {
                // WHAT??? Who's calling the get before we call the CreateInstance()?
                throw new InvalidOperationException("Client Pipe Instance gotten before set");
            }

            return _instance;
        }
    }

    public static void CreateInstance()
    {
        Log.Information($"ClientPipe - we are now creating instance for use.");
        _instance = new ClientPipe();
        _instanceCreated = true;
    }

    public ClientPipe Connect(string servicePipeName = Common.kGRDServicePipeName)
    {
        try
        {
            _clientStream = new NamedPipeClientStream(".", servicePipeName, PipeDirection.InOut);
            _clientStream.Connect();
            ss = new StreamString(_clientStream);
            var testACK = ss.ReadString();
            Log.Information($"Client Pipe connected to Service - received '{testACK}'");
        }
        catch (Exception e)
        {
            Log.Error(e, $"Exception when Connecting on pipe: {e.Message}");
            throw;
        }

        return _instance;
    }

    public string GetData(int value)
    {
        throw new NotImplementedException();
    }

    public IGuardianNPContract.CompositeType GetDataUsingDataContract(IGuardianNPContract.CompositeType composite)
    {
        var cmdPayload = JsonConvert.SerializeObject(composite);
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.GetDataUsingDataContract}.{cmdPayload}";
        ss.WriteString(cmdString);
        var response = ss.ReadString();
        var value = JsonConvert.DeserializeObject<IGuardianNPContract.CompositeType>(response);

        return value;
    }

    public bool StartVPNConnection(Dictionary<string, object> protocolRequest)
    {
        var cmdPayload = JsonConvert.SerializeObject(protocolRequest);
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.StartVPNConnection}.{cmdPayload}";
        ss.WriteString(cmdString);
        var started = ss.ReadString();

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
        var statusString = ss.ReadString();
        var status = JsonConvert.DeserializeObject<IGuardianNPContract.CurrentVPNStatus>(statusString);
        Log.Information($"status is {status.EntryName}, {status.ConnectionState}...");

        return status;
    }

    public string Ping()
    {
        Log.Information("Pinging service");
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.Ping}.";
        ss.WriteString(cmdString);
        Log.Information("Reading status...");
        var ping = ss.ReadString();
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

    public List<string> GetServiceLogLines(int maxNumberOfLinesToGet = 200)
    {
        Log.Information($"Requesting GuardianFirewall Service's last {maxNumberOfLinesToGet} log lines...");
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.RequestLogLines}.{maxNumberOfLinesToGet}";
        ss.WriteString(cmdString);
        Log.Information("Reading response...");
        var serializedServiceLogLines = ss.ReadString();
        var serviceLogLines = JsonConvert.DeserializeObject<List<string>>(serializedServiceLogLines);
        Log.Information($"Number of log lines returned from the service = {serviceLogLines.Count}");

        return serviceLogLines;
    }
}
