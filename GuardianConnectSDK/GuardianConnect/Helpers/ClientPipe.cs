using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Text.Json;
using GuardianConnect.Abstractions;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GuardianConnect.Helpers;

public static class ClientPipe
{
    internal static int SecondsToWaitForServiceConnect = 10;

    private static readonly ClientPipeImpl Instance = new();
    private static ILogger _logger = NullLogger.Instance;

    public static ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance) _logger = StaticLoggerFactory.CreateLogger("ClientPipe");
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

    public static async Task<ErrorResponse> StartVPNConnection(VPNCallParameters protocolRequest)
    {
        GRDVPNHelper.Logger.LogInformation(
            "ClientPipe.StartVPNConnection: Starting VPN connection via pipe...[12120934]");
        Logger.LogInformation(
            $"ClientPipe.StartVPNConnection: Checking pipe connection...Instance.IsConnected={Instance.IsConnected}");
        if (!Instance.IsConnected) Instance.ReopenNamedPipe();
        var startResponse = await Instance.StartVPNConnection(protocolRequest);
        Logger.LogInformation($"ClientPipe.StartVPNConnection: startResponse .IsError={startResponse.IsError}");
        return startResponse;
    }

    public static ErrorResponse DisconnectVPNConnection(string entryName)
    {
        var errorResponse = new ErrorResponse();
        try
        {
            if (!Instance.IsConnected) Instance.ReopenNamedPipe();
            errorResponse = Instance.DisconnectVPNConnection();
        }
        catch (Exception e)
        {
            Logger.LogError(e,
                $"ClientPipe.DisconnectVPNConnection: Exception when disconnecting VPN connection: {e.Message}");
            errorResponse.SetException(e);
            if (e is IOException)
            {
                Logger.LogError("ClientPipe.DisconnectVPNConnection: IOException detected");
                errorResponse.Message = "PIPE BROKEN";
            }
        }

        return errorResponse;
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
    private static NamedPipeClientStream _clientStream = new("NULL");
    private static StreamString ss = new(new NamedPipeClientStream("NULL"));
    private static int usingResource;

    internal ClientPipeImpl()
    {
    }

    internal bool IsConnected => _clientStream.IsConnected;

    string IGuardianNPContract.GetData(int value)
    {
        throw new NotImplementedException();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "CompositeType is a legacy test type not used in production AOT paths")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "CompositeType is a legacy test type not used in production AOT paths")]
    public CompositeType GetDataUsingDataContract(CompositeType composite)
    {
        var cmdPayload = JsonSerializer.Serialize(composite);
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.GetDataUsingDataContract}.{cmdPayload}";
        ss.WriteString(cmdString);
        var response = ss.ReadStringAsync().Result;
        var value = JsonSerializer.Deserialize<CompositeType>(response);

        if (value == null) throw new InvalidOperationException("Service returned null");

        return value;
    }

    public async Task<ErrorResponse> StartVPNConnection(VPNCallParameters protocolRequest)
    {
        ClientPipe.Logger.LogInformation(
            "ClientPipeImpl.StartVPNConnection: Sending StartVPNConnection command to service...");
        var startedErrorResponse = new ErrorResponse();
        var startedJson = "";
        try
        {
            var cmdPayload =
                JsonSerializer.Serialize(protocolRequest, VPNCallParametersJsonContext.Default.VPNCallParameters);
            var cmdString = $"{(int)IGuardianNPContract.NPCommands.StartVPNConnection}.{cmdPayload}";
            ss.WriteString(cmdString);
            ClientPipe.Logger.LogInformation("ClientPipeImpl.StartVPNConnection: command sent to service.");
            startedJson = await ss.ReadStringAsync();
            ClientPipe.Logger.LogInformation("ClientPipeImpl.StartVPNConnection: Received response from service.");

            startedErrorResponse =
                JsonSerializer.Deserialize<ErrorResponse>(startedJson,
                    ErrorResponseJsonContext.Default.ErrorResponse) ?? new ErrorResponse();
            if (startedErrorResponse.IsError)
                ClientPipe.Logger.LogError(
                    $"ClientPipe.StartVPNConnection - error response from service: is '{startedJson}'");
            ClientPipe.Logger.LogInformation("ClientPipeImpl.StartVPNConnection: returning to caller.");
        }
        catch (Exception e)
        {
            ClientPipe.Logger.LogError(e,
                $"ClientPipe.StartVPNConnection: Exception when parsing response from pipe: {e.Message}. Raw json='{startedJson}'");
            startedErrorResponse.SetException(e);
            if (e is IOException)
            {
                ClientPipe.Logger.LogError("ClientPipe.StartVPNConnection: IOException detected");
                startedErrorResponse.Message = "PIPE BROKEN";
            }
        }


        return startedErrorResponse;
    }

    public ErrorResponse DisconnectVPNConnection()
    {
        var errorResponse = new ErrorResponse();
        try
        {
            var cmdPayload = "";
            var cmdString = $"{(int)IGuardianNPContract.NPCommands.DisconnectVPNConnection}.{cmdPayload}";
            ss.WriteString(cmdString);
        }
        catch (Exception e)
        {
            ClientPipe.Logger.LogError(e,
                $"ClientPipe.DisconnectVPNConnection: Exception when disconnecting VPN connection: {e.Message}");
            errorResponse.SetException(e);
            if (e is IOException)
            {
                ClientPipe.Logger.LogError("ClientPipe.DisconnectVPNConnection: IOException detected");
                errorResponse.Message = "PIPE BROKEN";
            }
        }

        return errorResponse;
    }

    public CurrentVPNStatus GetCurrentVpnConnectionStatus()
    {
        ClientPipe.Logger.LogInformation("Calling service to GetCurrentVpnConnectionStatus...");
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.GetCurrentVpnConnectionStatus}.";
        ss.WriteString(cmdString);
        ClientPipe.Logger.LogInformation("Reading status...");
        //var statusString = ss.ReadStringAsync().Result;
        var statusString = ss.ReadString();
        var status =
            JsonSerializer.Deserialize<CurrentVPNStatus>(statusString,
                CurrentVPNStatusJsonConect.Default.CurrentVPNStatus)
            ?? new CurrentVPNStatus();
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

    public void SwitchServiceLoggingLevel(Common.LoggingLevels loggingLevel)
    {
        ClientPipe.Logger.LogWarning($"Sending command to service to switch logging level to {loggingLevel}");
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.SwitchLoggingLevel}.{loggingLevel}";
        ss.WriteString(cmdString);
    }

    internal void OpenNamedPipe(string servicePipeName = Common.kGRDServicePipeName)
    {
        Exception? lastThrownException = null;
        ClientPipe.Logger.LogInformation($"ClientPipeImpl.OpenNamedPipe: Entered...[{usingResource}]");
        if (0 == Interlocked.Exchange(ref usingResource, 1))
        {
            ClientPipe.Logger.LogInformation("ClientPipeImpl.OpenNamedPipe: Opening Pipe Stream...");
            _clientStream = new NamedPipeClientStream(".", servicePipeName, PipeDirection.InOut);
            ClientPipe.Logger.LogInformation(
                "ClientPipeImpl.OpenNamedPipe: Going into Opening loop until success or retries exhausted...");
            var retries = 10;
            while (retries-- > 0 && !_clientStream.IsConnected)
                try
                {
                    _clientStream.Connect(ClientPipe.SecondsToWaitForServiceConnect * 1000);
                    ClientPipe.Logger.LogInformation(
                        $"ClientPipeImpl.OpenNamedPipe: {retries} left to attempt opening of Client Pipe Stream...");
                }
                catch (Exception e)
                {
                    ClientPipe.Logger.LogError(e,
                        $"ClientPipeImpl.OpenNamedPipe: Exception when connecting to Pipe Stream: {e.Message}. Retries left={retries}");
                    lastThrownException = e;
                }

            if (!IsConnected)
            {
                ClientPipe.Logger.LogError(
                    "!!!!!!!!!!!!!!!!!!!! ClientPipeImpl.OpenNamedPipe could NOT connect to Pipe Stream...");
                throw lastThrownException ??
                      new InvalidOperationException("Could not connect to Named Pipe Stream");
            }
        }

        usingResource = 0;
        ClientPipe.Logger.LogInformation($"ClientPipeImpl.OpenNamedPipe: Exiting...[{usingResource}]");
    }

    internal void ReopenNamedPipe()
    {
        ClientPipe.Logger.LogWarning("!!!!!!!!!!!!!! REOPENING CLIENTPIPE TO SERVICE...");
        OpenNamedPipe();
        ss = new StreamString(_clientStream);
    }

    internal bool Connect(string servicePipeName = Common.kGRDServicePipeName)
    {
        var whetherPreviouslyConnectedAtSuspend = false;
        try
        {
            ClientPipe.Logger.LogInformation("ClientPipeImpl.Connect: Calling OpenNamedPipe...");
            OpenNamedPipe(servicePipeName);
            ss = new StreamString(_clientStream);
            //var testAck = ss.ReadStringAsync();
            var testAck = ss.ReadString();
            ClientPipe.Logger.LogInformation($"Client Pipe connected to Service. testAck returned '{testAck}'");
            var pieces = testAck.Split(new[] { '#' });
#if I429FIXED
            whetherPreviouslyConnectedAtSuspend =
 pieces.Length > 2 && pieces[2].Equals("true", StringComparison.InvariantCultureIgnoreCase);
#else
            whetherPreviouslyConnectedAtSuspend = false;
#endif
        }
        catch (Exception e)
        {
            ClientPipe.Logger.LogError(e, $"Exception when Connecting on pipe: {e.Message}");
            throw;
        }

        return whetherPreviouslyConnectedAtSuspend;
    }

    public async Task<List<string>> GetServiceLogLinesAsync(int maxNumberOfLinesToGet = 200)
    {
        ClientPipe.Logger.LogInformation(
            $"Requesting GuardianFirewall Service's last {maxNumberOfLinesToGet} log lines...");
        var cmdString = $"{(int)IGuardianNPContract.NPCommands.RequestLogLines}.{maxNumberOfLinesToGet}";
        ss.WriteString(cmdString);
        ClientPipe.Logger.LogInformation("Reading response...");
        var serializedServiceLogs = await ss.ReadStringAsync();
        var jsonLines =
            JsonSerializer.Deserialize<List<string>>(serializedServiceLogs, LogLinesJsonContext.Default.ListString);
        var serviceLogLines = jsonLines ?? new List<string>();
        ClientPipe.Logger.LogInformation($"Number of log lines returned from the service = {serviceLogLines.Count}");

        return serviceLogLines;
    }
}