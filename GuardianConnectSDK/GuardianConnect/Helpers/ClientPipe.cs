using System.IO.Pipes;
using System.Text.Json;
using GuardianConnect.Shared;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Serilog;

namespace GuardianConnect.Helpers;

public static class ClientPipe
{
    private static readonly ClientPipeImpl Instance = new ClientPipeImpl();

    public static bool Connect(string servicePipeName = Common.kGRDServicePipeName)
    {
        return Instance.Connect(servicePipeName);
    }

    public static IGuardianNPContract.CompositeType GetDataUsingDataContract(IGuardianNPContract.CompositeType composite)
    {
        if (!Instance.IsConnected) Instance.OpenNamedPipe();
        return Instance.GetDataUsingDataContract(composite);
    }

    public static ErrorResponse StartVPNConnection(VPNCallParameters protocolRequest)
    {
        if (!Instance.IsConnected) Instance.OpenNamedPipe();
        return Instance.StartVPNConnection(protocolRequest);
    }

    public static void DisconnectVPNConnection(string entryName)
    {
        if (!Instance.IsConnected) Instance.OpenNamedPipe();
        Instance.DisconnectVPNConnection();
    }

    public static CurrentVPNStatus GetCurrentVpnConnectionStatus()
    {
        if (!Instance.IsConnected) Instance.OpenNamedPipe();
        return Instance.GetCurrentVpnConnectionStatus();
    }

    public static async Task<string> Ping()
    {
        if (!Instance.IsConnected) Instance.OpenNamedPipe();
        return await Instance.Ping();
    }

    public static void ToggleLogging(bool whetherToDeleteLogFiles)
    {
        if (!Instance.IsConnected) Instance.OpenNamedPipe();
        Instance.ToggleLogging(whetherToDeleteLogFiles);

    }

    public static async Task<List<string>> GetServiceLogLinesAsync(int maxNumberOfLinesToGet = 200)
    {
        if (!Instance.IsConnected) Instance.OpenNamedPipe();
        return await Instance.GetServiceLogLinesAsync(maxNumberOfLinesToGet);
    }

    public static void SwitchServiceLoggingLevel(Common.LoggingLevels loggingLevel)
    {
        if (!Instance.IsConnected) Instance.OpenNamedPipe();
        Instance.SwitchServiceLoggingLevel(loggingLevel);
    }
        
}

public class ClientPipeImpl : IGuardianNPContract
{
    private static NamedPipeClientStream _clientStream = new NamedPipeClientStream("NULL");
    private static StreamString ss;

    internal bool IsConnected => _clientStream.IsConnected;

    internal void OpenNamedPipe(string servicePipeName = Common.kGRDServicePipeName)
    {
        Log.Information("ClientPipeImpl.OpenNamedPipe: Opening Pipe Stream...");
        _clientStream = new NamedPipeClientStream(".", servicePipeName, PipeDirection.InOut);
        Log.Information("ClientPipeImpl.OpenNamedPipe: Going into Opening loop until success or retries exhausted...");
        int retries = 10;
        while (retries-- > 0 && !_clientStream.IsConnected)
        {
            try
            {
                _clientStream.Connect(10 * 1000);
                Log.Information($"ClientPipeImpl.OpenNamedPipe: {retries} left to attempt opening of Client Pipe Stream...");
            }
            catch (Exception e)
            {
                if (!IsConnected)
                {
                    Log.Error("!!!!!!!!!!!!!!!!!!!! ClientPipeImpl.OpenNamedPipe could NOT connect to Pipe Stream...");
                    throw;
                }
            }
        }
    }
    
    internal bool Connect(string servicePipeName = Common.kGRDServicePipeName)
    {
        bool whetherPreviouslyConnectedAtSuspend = false;
        try
        {
            Log.Information("ClientPipeImpl.Connect: Calling OpenNamedPipe...");
            OpenNamedPipe(servicePipeName);
            ss = new StreamString(_clientStream);
            //var testAck = ss.ReadStringAsync();
            var testAck = ss.ReadString();
            Log.Information($"Client Pipe connected to Service. testAck returned '{testAck}'");
            var pieces = testAck.Split(new char[] { '#' } );
            whetherPreviouslyConnectedAtSuspend = pieces[2].Equals("true", StringComparison.InvariantCultureIgnoreCase);
        }
        catch (Exception e)
        {
            Log.Error(e, $"Exception when Connecting on pipe: {e.Message}");
            throw;
        }

        return whetherPreviouslyConnectedAtSuspend;
    }

    string IGuardianNPContract.GetData(int value)
    {
        throw new NotImplementedException();
    }

    public IGuardianNPContract.CompositeType GetDataUsingDataContract(IGuardianNPContract.CompositeType composite)
    {
        var cmdPayload = JsonSerializer.Serialize(composite);
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.GetDataUsingDataContract}.{cmdPayload}";
        ss.WriteString(cmdString);
        var response = ss.ReadStringAsync().Result;
        var value = JsonSerializer.Deserialize<IGuardianNPContract.CompositeType>(response);

        if (value == null)
        {
            throw new InvalidOperationException("Service returned null");
        }

        return value;
    }

    public ErrorResponse StartVPNConnection(VPNCallParameters protocolRequest)
    {
        var cmdPayload = JsonSerializer.Serialize(protocolRequest, VPNCallParametersJsonContext.Default.VPNCallParameters);
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.StartVPNConnection}.{cmdPayload}";
        ss.WriteString(cmdString);
        var startedJson = ss.ReadStringAsync().Result;
        Log.Information($"ClientPipe.StartVPNConnection - string is '{startedJson}'");

        ErrorResponse startedErrorResponse = new ErrorResponse();
        try
        {
            startedErrorResponse = JsonSerializer.Deserialize<ErrorResponse>(startedJson, ErrorResponseJsonContext.Default.ErrorResponse);
        }
        catch (Exception e)
        {
            Log.Error(e, $"ClientPipe.StartVPNConnection: Exception when parsing response from pipe: {e.Message}");
        }

        return startedErrorResponse;
    }

    public void DisconnectVPNConnection()
    {
        var cmdPayload = "";
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.DisconnectVPNConnection}.{cmdPayload}";
        ss.WriteString(cmdString);
    }

    public CurrentVPNStatus GetCurrentVpnConnectionStatus()
    {
        Log.Information("Calling service to GetCurrentVpnConnectionStatus...");
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.GetCurrentVpnConnectionStatus}.";
        ss.WriteString(cmdString);
        Log.Information("Reading status...");
        //var statusString = ss.ReadStringAsync().Result;
        var statusString = ss.ReadString();
        var status = JsonSerializer.Deserialize<CurrentVPNStatus>(statusString, CurrentVPNStatusJsonConect.Default.CurrentVPNStatus);
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
        var jsonLines = JsonSerializer.Deserialize<List<string>>(serializedServiceLogLines, Common.DefaultJsonSerializerOptions);
        var serviceLogLines = jsonLines ?? new List<string>();
        Log.Information($"Number of log lines returned from the service = {serviceLogLines.Count}");

        return serviceLogLines;
    }

    public void SwitchServiceLoggingLevel(Common.LoggingLevels loggingLevel)
    {
        Log.Warning($"Sending command to service to switch logging level to {loggingLevel}");
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.SwitchLoggingLevel}.{loggingLevel}";
        ss.WriteString(cmdString);
        
    }
}
