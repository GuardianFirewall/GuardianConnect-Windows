using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Net.NetworkInformation;
using System.Text.Json;
using GuardianConnect.Abstractions;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

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
    
    // Send Power and Network Change messages to server
    public static void SendPowerAndNetworkChangeMessages(Dictionary<string, object> systemEventsDict)
    {
        if (!Instance.IsConnected) Instance.ReopenNamedPipe();
        Instance.SendPowerAndNetworkChangeEvents(systemEventsDict);
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

    private char Hexify(IGuardianNPContract.NPCommands command)
    {
        return (char)(command + '0');
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
        var cmdString = $"{Hexify(IGuardianNPContract.NPCommands.GetDataUsingDataContract)}.{cmdPayload}";
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
            var cmdPayload = JsonSerializer.Serialize(protocolRequest, VPNCallParametersJsonContext.Default.VPNCallParameters);
            var cmdString = $"{Hexify(IGuardianNPContract.NPCommands.StartVPNConnection)}.{cmdPayload}";
            ss.WriteString(cmdString);
            ClientPipe.Logger.LogInformation("ClientPipeImpl.StartVPNConnection: command sent to service.");
            startedJson = await ss.ReadStringAsync();
            startedJson = startedJson.TrimEnd('\0');
            if (!startedJson.StartsWith('{')) startedJson = "{ " + startedJson;
            //ClientPipe.Logger.LogInformation("ClientPipeImpl.StartVPNConnection: Received response from service");
            ClientPipe.Logger.LogInformation($"ClientPipeImpl.StartVPNConnection: Received response from service: '{startedJson}'");

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
            var cmdString = $"{Hexify(IGuardianNPContract.NPCommands.DisconnectVPNConnection)}.{cmdPayload}";
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
        var cmdString = $"{Hexify(IGuardianNPContract.NPCommands.GetCurrentVpnConnectionStatus)}.";
        ss.WriteString(cmdString);
        ClientPipe.Logger.LogInformation("Reading status...");
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
        var cmdString = $"{Hexify(IGuardianNPContract.NPCommands.Ping)}.";
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
        var cmdString = $"{Hexify(IGuardianNPContract.NPCommands.ToggleLogging)}.{whetherToDeleteLogFiles.ToString()}";
        ss.WriteString(cmdString);
    }

    public void SwitchServiceLoggingLevel(Common.LoggingLevels loggingLevel)
    {
        ClientPipe.Logger.LogWarning($"Sending command to service to switch logging level to {loggingLevel}");
        var cmdString = $"{Hexify(IGuardianNPContract.NPCommands.SwitchLoggingLevel)}.{loggingLevel}";
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
        var cmdString = $"{Hexify(IGuardianNPContract.NPCommands.RequestLogLines)}.{maxNumberOfLinesToGet}";
        ss.WriteString(cmdString);
        ClientPipe.Logger.LogInformation("Reading response...");
        var serializedServiceLogs = await ss.ReadStringAsync();
        var jsonLines =
            JsonSerializer.Deserialize<List<string>>(serializedServiceLogs, LogLinesJsonContext.Default.ListString);
        var serviceLogLines = jsonLines ?? new List<string>();
        ClientPipe.Logger.LogInformation($"Number of log lines returned from the service = {serviceLogLines.Count}");

        return serviceLogLines;
    }

    public async Task<ErrorResponse> SendPowerAndNetworkChangeEvents( Dictionary<string, object> systemEventsDict)
    {
        if (systemEventsDict == null)
        {
            throw new ArgumentNullException(nameof(systemEventsDict), "systemEventsDict cannot be null");
        }

        // We're going to split this out to preserve argments and not deal with 'too-generic' json hassle
        var sender = systemEventsDict.Keys.First();
        var o = systemEventsDict[sender];
        int senderEventType = 0;
        var cmdPayload = "";
        switch (sender)
        {
            case "Client_PowerModeChangeEvent":
                senderEventType = (int)IGuardianNPContract.SystemEventType.PowerModeChangeEvent;
                try
                {
                    if (o == null || !(o is PowerModeChangedEventArgs))
                    {
                        ClientPipe.Logger.LogError($"Invalid event data for PowerModeChangeEvent: {o?.GetType().FullName}");
                        return new ErrorResponse();
                    }
                    cmdPayload = JsonSerializer.Serialize<PowerModeChangedEventArgs>((PowerModeChangedEventArgs)o, PowerModeChangedEventArgsContext.Default.PowerModeChangedEventArgs);
                }
                catch (Exception e)
                {
                    ClientPipe.Logger.LogError($"Error serializing PowerModeChangedEventArgs: {e.Message}");
                }
                break;
            case "Client_PowerChangeNotifyCallbackRoutine":
                senderEventType = (int)IGuardianNPContract.SystemEventType.PowerChangeNotifyNotificationEvent;
                try
                {
                    cmdPayload = JsonSerializer.Serialize((Tuple<int, uint, int>)o, PowerChangeNotifyTupleContext.Default.TupleInt32UInt32Int32);
                }
                catch (Exception e)
                {
                    ClientPipe.Logger.LogError($"Error serializing PowerChangeNotifyCallbackRoutine event: {e.Message}");
                }
                break;
            
            case "Client_NetworkAddressChanged":
                senderEventType = (int)IGuardianNPContract.SystemEventType.NetworkChangeOnNetworkAddressChanged;
                cmdPayload = ""; // The EventArgs passed in on the NetworkAddressChanged event is always empty.
                break;
            case "Client_NetworkAvailabilityChange":
                senderEventType = (int)IGuardianNPContract.SystemEventType.NetworkChangeOnNetworkAvailabilityChanged;
                cmdPayload = JsonSerializer.Serialize((NetworkAvailabilityEventArgs)o, NetworkAvailabilityEventArgsContext.Default.NetworkAvailabilityEventArgs);
                break;
        }
        var cmdString = $"{Hexify(IGuardianNPContract.NPCommands.SendPowerAndNetworkEvents)}{senderEventType}.{cmdPayload}";
        ss.WriteString(cmdString);
        // Don't think we need a response from Service on what we threw over to it - basically a fire and forget
        //var response = ss.ReadStringAsync().Result;
        //ClientPipe.Logger.Log(LogLevel.Information, $"ClientPipe: string from service: '{response}'");
        //var value = JsonSerializer.Deserialize<CompositeType>(response);

        return new ErrorResponse();
    }
}