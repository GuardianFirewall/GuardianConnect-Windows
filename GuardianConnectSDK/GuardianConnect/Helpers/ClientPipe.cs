using GuardianConnect.Abstractions;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using System.Collections;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace GuardianConnect.Helpers;

public static class ClientPipe
{
    private static readonly ClientPipeImpl Instance = new ClientPipeImpl();
    private static Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;
    public static Microsoft.Extensions.Logging.ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance)
            {
                _logger = StaticLoggerFactory.CreateLogger("ClientPipe");
            }
            return _logger;
        }
    }

    public static bool Connect(string servicePipeName = Common.kGRDServicePipeName)
    {
        return Instance.Connect(servicePipeName);
    }

    public static CompositeType GetDataUsingDataContract(CompositeType composite)
    {
        if (!Instance.IsConnected) Instance.ReopenNamedPipe();
        return Instance.GetDataUsingDataContract(composite);
    }

    public static ErrorResponse StartVPNConnection(VPNCallParameters protocolRequest)
    {
        if (!Instance.IsConnected) Instance.ReopenNamedPipe();
        return Instance.StartVPNConnection(protocolRequest);
    }

    public static void DisconnectVPNConnection(string entryName)
    {
        if (!Instance.IsConnected) Instance.ReopenNamedPipe();
        Instance.DisconnectVPNConnection();
    }

    public static CurrentVPNStatus GetCurrentVpnConnectionStatus()
    {
        if (!Instance.IsConnected) Instance.ReopenNamedPipe();
        return Instance.GetCurrentVpnConnectionStatus();
    }

    public static async Task<string> Ping()
    {
        if (!Instance.IsConnected) Instance.ReopenNamedPipe();
        return await Instance.Ping();
    }

    public static void ToggleLogging(bool whetherToDeleteLogFiles)
    {
        if (!Instance.IsConnected) Instance.ReopenNamedPipe();
        Instance.ToggleLogging(whetherToDeleteLogFiles);

    }

    public static async Task<List<string>> GetServiceLogLinesAsync(int maxNumberOfLinesToGet = 200)
    {
        if (!Instance.IsConnected) Instance.ReopenNamedPipe();
        return await Instance.GetServiceLogLinesAsync(maxNumberOfLinesToGet);
    }

    public static void SwitchServiceLoggingLevel(Common.LoggingLevels loggingLevel)
    {
        if (!Instance.IsConnected) Instance.ReopenNamedPipe();
        Instance.SwitchServiceLoggingLevel(loggingLevel);
    }
        
}

public class ClientPipeImpl : IGuardianNPContract
{
    private static NamedPipeClientStream _clientStream = new NamedPipeClientStream("NULL");
    private static StreamString ss;
    private static int usingResource = 0;

    internal ClientPipeImpl()
    {
    }

    internal bool IsConnected => _clientStream.IsConnected;

    internal void OpenNamedPipe(string servicePipeName = Common.kGRDServicePipeName)
    {
        if (0 == Interlocked.Exchange(ref usingResource, 1))
        {
            ClientPipe.Logger.LogInformation("ClientPipeImpl.OpenNamedPipe: Opening Pipe Stream...");
            _clientStream = new NamedPipeClientStream(".", servicePipeName, PipeDirection.InOut);
            ClientPipe.Logger.LogInformation(
                "ClientPipeImpl.OpenNamedPipe: Going into Opening loop until success or retries exhausted...");
            int retries = 10;
            while (retries-- > 0 && !_clientStream.IsConnected)
            {
                try
                {
                    _clientStream.Connect(30 * 1000);
                    ClientPipe.Logger.LogInformation(
                        $"ClientPipeImpl.OpenNamedPipe: {retries} left to attempt opening of Client Pipe Stream...");
                }
                catch (Exception e)
                {
                    if (!IsConnected)
                    {
                        ClientPipe.Logger.LogError(
                            "!!!!!!!!!!!!!!!!!!!! ClientPipeImpl.OpenNamedPipe could NOT connect to Pipe Stream...");
                        throw;
                    }
                }
            }
        }
    }

    internal void ReopenNamedPipe()
    {
        ClientPipe.Logger.LogWarning("!!!!!!!!!!!!!! REOPENING CLIENTPIPE TO SERVICE...");
        OpenNamedPipe();
        ss = new StreamString(_clientStream);
    }
    
    internal bool Connect(string servicePipeName = Common.kGRDServicePipeName)
    {
        bool whetherPreviouslyConnectedAtSuspend = false;
        try
        {
            ClientPipe.Logger.LogInformation($"ClientPipeImpl.Connect: Calling OpenNamedPipe...");
            OpenNamedPipe(servicePipeName);
            ss = new StreamString(_clientStream);
            //var testAck = ss.ReadStringAsync();
            var testAck = ss.ReadString();
            ClientPipe.Logger.LogInformation($"Client Pipe connected to Service. testAck returned '{testAck}'");
            var pieces = testAck.Split(new char[] { '#' } );
            whetherPreviouslyConnectedAtSuspend = pieces[2].Equals("true", StringComparison.InvariantCultureIgnoreCase);
        }
        catch (Exception e)
        {
            ClientPipe.Logger.LogError(e, $"Exception when Connecting on pipe: {e.Message}");
            throw;
        }

        return whetherPreviouslyConnectedAtSuspend;
    }

    string IGuardianNPContract.GetData(int value)
    {
        throw new NotImplementedException();
    }

    public CompositeType GetDataUsingDataContract(CompositeType composite)
    {
        var cmdPayload = JsonSerializer.Serialize(composite);
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.GetDataUsingDataContract}.{cmdPayload}";
        ss.WriteString(cmdString);
        var response = ss.ReadStringAsync().Result;
        var value = JsonSerializer.Deserialize<CompositeType>(response);

        if (value == null)
        {
            throw new InvalidOperationException("Service returned null");
        }

        return value;
    }

    public ErrorResponse StartVPNConnection(VPNCallParameters? protocolRequest)
    {
        var cmdPayload = JsonSerializer.Serialize(protocolRequest, VPNCallParametersJsonContext.Default.VPNCallParameters);
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.StartVPNConnection}.{cmdPayload}";
        ss.WriteString(cmdString);
        var startedJson = ss.ReadStringAsync().Result;

        ErrorResponse startedErrorResponse = new ErrorResponse();
        try
        {
            startedErrorResponse = JsonSerializer.Deserialize<ErrorResponse>(startedJson, ErrorResponseJsonContext.Default.ErrorResponse);
        }
        catch (Exception e)
        {
            ClientPipe.Logger.LogError(e, $"ClientPipe.StartVPNConnection: Exception when parsing response from pipe: {e.Message}. Raw json='{startedJson}'");
        }

        if (startedErrorResponse.IsError)
        {
            ClientPipe.Logger.LogError( $"ClientPipe.StartVPNConnection - error response from service: is '{startedJson}'");
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
        ClientPipe.Logger.LogInformation("Calling service to GetCurrentVpnConnectionStatus...");
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.GetCurrentVpnConnectionStatus}.";
        ss.WriteString(cmdString);
        ClientPipe.Logger.LogInformation("Reading status...");
        //var statusString = ss.ReadStringAsync().Result;
        var statusString = ss.ReadString();
        var status = JsonSerializer.Deserialize<CurrentVPNStatus>(statusString, CurrentVPNStatusJsonConect.Default.CurrentVPNStatus);
        ClientPipe.Logger.LogInformation($"status is {status.EntryName}, {status.ConnectionState}...");

        return status;
    }

    public async Task<string> Ping()
    {
        ClientPipe.Logger.LogInformation("Pinging service");
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.Ping}.";
        ss.WriteString(cmdString);
        ClientPipe.Logger.LogInformation("Reading status...");
        var ping = await ss.ReadStringAsync();
        ClientPipe.Logger.LogInformation($"Service returned {ping}");
        return ping;
    }

    public void ShutdownService()
    {
        throw new NotImplementedException();
    }

    public void ToggleLogging(bool whetherToDeleteLogFiles)
    {
        var msg = Common.LogFilterOn ? "OFF" : "ON";
        ClientPipe.Logger.LogInformation($"Telling Service to turn Logging {msg}");
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.ToggleLogging}.{whetherToDeleteLogFiles.ToString()}";
        ss.WriteString(cmdString);
    }

    public async Task<List<string>> GetServiceLogLinesAsync(int maxNumberOfLinesToGet = 200)
    {
        ClientPipe.Logger.LogInformation($"Requesting GuardianFirewall Service's last {maxNumberOfLinesToGet} log lines...");
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.RequestLogLines}.{maxNumberOfLinesToGet}";
        ss.WriteString(cmdString);
        ClientPipe.Logger.LogInformation("Reading response...");
        var serializedServiceLogs = await ss.ReadStringAsync();
        var jsonLines = JsonSerializer.Deserialize<List<string>>(serializedServiceLogs, LogLinesJsonContext.Default.ListString);
        var serviceLogLines = jsonLines ?? new List<string>();
        ClientPipe.Logger.LogInformation($"Number of log lines returned from the service = {serviceLogLines.Count}");

        return serviceLogLines;
    }

    public void SwitchServiceLoggingLevel(Common.LoggingLevels loggingLevel)
    {
        ClientPipe.Logger.LogWarning($"Sending command to service to switch logging level to {loggingLevel}");
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.SwitchLoggingLevel}.{loggingLevel}";
        ss.WriteString(cmdString);
        
    }
}
