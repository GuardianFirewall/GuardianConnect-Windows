using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using GuardianConnect.Abstractions;
using GuardianConnect.Shared;
using GuardianFirewallService;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Win32Calls;

namespace GuardianConnect.Services;

public class ClientPipeService : BackgroundService
{
    private static int numThreads = 32;
    private static Thread?[] servers = new Thread[numThreads];
    private static CancellationToken _cancellationToken;
    private static bool AdministrativeShutdownRequested = false;
    private readonly ILogger<ClientPipeService> _logger;
    internal static int NumberOfClientsConnected;

    public ClientPipeService(ILogger<ClientPipeService> logger)
    {
        _logger = logger;
        _logger.Log(LogLevel.Information, "ClientPipeService: TESTING LOG");
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _cancellationToken = stoppingToken;
        _cancellationToken.ThrowIfCancellationRequested();
        _logger.Log(LogLevel.Information, "ClientPipeService running at: {time}", DateTimeOffset.Now);

        stoppingToken.Register(() => _logger.Log(LogLevel.Information, "ClientPipeService is stopping."));
        
        StartServerListeners();

        try
        {
            var heartbeatCounter = 0;
            var priorMessage =
                $"ClientPipeService is running... Clients connected: {NumberOfClientsConnected}. Cancellation Request is {stoppingToken.IsCancellationRequested}";
            _logger.Log(LogLevel.Information, 
                $"Going into while() loop. stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
            while (!stoppingToken.IsCancellationRequested)
            {
                var currentMessage =
                    $"ClientPipeService is running... Clients connected: {NumberOfClientsConnected}. Cancellation Request is {stoppingToken.IsCancellationRequested}";

                if (!currentMessage.Equals(priorMessage))
                {
                    _logger.Log(LogLevel.Information, currentMessage);
                    heartbeatCounter = 0;
                    priorMessage = currentMessage;
                }
                else if (++heartbeatCounter % 5 == 0) _logger.Log(LogLevel.Information, "ClientPipeService is running...");

                await Task.Delay(60000, stoppingToken);
            }

            _logger.Log(LogLevel.Information, 
                $"Past while() loop. stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");

            StopServerListenerThreads();
        }
        catch (OperationCanceledException oce) when (!oce.CancellationToken.IsCancellationRequested)
        {
            _logger.Log(LogLevel.Information, "OperationCanceledException");
            // When the stopping token is canceled, for example, a call made from services.msc,
            // we shouldn't exit with a non-zero exit code. In other words, this is expected...

            //_logger.LogError(oce, "{Message}", oce.Message);
            _logger.Log(LogLevel.Error,  oce.ToString());
            StopServerListenerThreads();
        }
        catch (Exception ex)
        {
            //_logger.LogError(ex, "{Message}", ex.Message);
            _logger.Log(LogLevel.Error, ex.ToString());

            // Terminates this process and returns an exit code to the operating system.
            // This is required to avoid the 'BackgroundServiceExceptionBehavior', which
            // performs one of two scenarios:
            // 1. When set to "Ignore": will do nothing at all, errors cause zombie services.
            // 2. When set to "StopHost": will cleanly stop the host, and log errors.
            //
            // In order for the Windows Service Management system to leverage configured
            // recovery options, we need to terminate the process with a non-zero exit code.
        }
        _logger.Log(LogLevel.Information, "ClientPipeService: past Task Creation clause...");
    }

    public override Task? ExecuteTask { get; }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        System.Diagnostics.StackTrace t = new System.Diagnostics.StackTrace();
        //_logger.Log(LogLevel.Information, $"StartAsync StackTrace: \n{t}");
        _logger.Log(LogLevel.Information, "StartAsync():");

        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        //System.Diagnostics.StackTrace t = new System.Diagnostics.StackTrace();
        //_logger.Log(LogLevel.Information, "ClientPipeService.StopAsync StackTrace: \n{t}");
        _logger.Log(LogLevel.Information, "ClientPipeService.StopAsync called.");

        return base.StopAsync(cancellationToken);
    }

    public void StartServerListeners()
    {
        int i;

        _logger.Log(LogLevel.Information, "\n*** Named pipe server stream ***\n");
        _logger.Log(LogLevel.Information, "Waiting for client connect...\n");
        for (i = 0; i < numThreads; i++)
        {
            servers[i] = new Thread(ServerThread);
            servers[i]?.Start();
        }
    }

    public void StopServerListenerThreads()
    {
        int i = numThreads;
        Thread.Sleep(50);
        while (i > 0)
        {
            for (int j = 0; j < numThreads; j++)
            {
                if (servers[j] != null)
                {
                    if (servers[j]!.Join(250))
                    {
                        _logger.Log(LogLevel.Information, "Server thread[{0}] finished.", servers[j]!.ManagedThreadId);
                        servers[j] = null;
                        i--; // decrement the thread watch count
                    }
                }
            }
        }

        _logger.Log(LogLevel.Information, "\nServer threads exhausted, exiting.");

    }

    public async void ServerThread(object? data)
    {
        GuardianNPCommandDispatcher cmdDispatcher = new GuardianNPCommandDispatcher();
        //cmdDispatcher.Logger = _logger;

        int threadId = Thread.CurrentThread.ManagedThreadId;

        while (!_cancellationToken.IsCancellationRequested && !AdministrativeShutdownRequested)
        {
            PipeSecurity pipeSecurity = new PipeSecurity();
            pipeSecurity.AddAccessRule(
                new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), PipeAccessRights.FullControl,
                    AccessControlType.Allow));

            //NamedPipeServerStream pipeServer = new NamedPipeServerStream("GuardianFirewallService", PipeDirection.InOut, numThreads);
            NamedPipeServerStream pipeServer = NamedPipeServerStreamAcl.Create("GuardianFirewallService",
                PipeDirection.InOut, numThreads, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                65536, 65536, pipeSecurity);

            // Wait for a client to connect
            _logger.Log(LogLevel.Information, $"Pipe Service Thread #{threadId} going to wait for Client Connection...");
            pipeServer.WaitForConnection();

            Interlocked.Increment(ref NumberOfClientsConnected);
            _logger.Log(LogLevel.Information, "Client connected on thread[{0}].", threadId);
            StreamString ss = new StreamString(pipeServer);
            // Verify our identity to the connected client using a
            // string that the client anticipates.
            var connectTestACKResponse = $"GuardianFirewallService#ACK#{ServicePowerEventsHandler.ConnectedAtSuspendTime()}";
            _logger.Log(LogLevel.Information, $"ClientPipeService[{threadId}]: Writing connection ACK string to client: '{connectTestACKResponse}'");
            ss.WriteString(connectTestACKResponse);

            while (pipeServer.IsConnected && !_cancellationToken.IsCancellationRequested && !AdministrativeShutdownRequested)
            {
                try
                {
                    // We're going to try looping here until we're told to shut down
                    // We will accept a command from the client side in the format
                    // N.paramsString
                    // Read the request from the client. Once the client has
                    // written to the pipe its security token will be available.

                    // Now - wait for command string from client
                    _logger.Log(LogLevel.Information, $"ClientPipeService[{threadId}]: Waiting for command from client ...");
                    string commandString = ss.ReadStringAsync().GetAwaiter().GetResult();
                    _logger.Log(LogLevel.Information, $"ClientPipeService[{threadId}]: string from client: {commandString}");
                    if (!pipeServer.IsConnected) continue;
                    char cmdToken = commandString[0];
                    string cmdPayload = commandString.Substring(2); // Skip the '.' between first char cmd enum and params data
                    IGuardianNPContract.NPCommands cmd = (IGuardianNPContract.NPCommands)(Int16.Parse(cmdToken.ToString()));

                    _logger.Log(LogLevel.Information, $"ClientPipeService[{threadId}]: Cmd={cmd}, payload='{cmdPayload}");
                    switch (cmd)
                    {
                        case IGuardianNPContract.NPCommands.StartVPNConnection:
                            _logger.Log(LogLevel.Information, $"ClientPipeService[{threadId}][12121026]: Performing spawn of StartVPNConnection command");
                            var serializedVpnParameters = cmdPayload;
                            var vpnCallParameters = JsonSerializer.Deserialize<VPNCallParameters>(serializedVpnParameters, VPNCallParametersJsonContext.Default.VPNCallParameters);
                            try
                            {
                                var didItStart = await cmdDispatcher.StartVPNConnection(vpnCallParameters!);
                                _logger.Log(LogLevel.Information, $"ClientPipeService.StartVPNConnection - response IsError: {didItStart.IsError}");
                                var startResponseJson = JsonSerializer.Serialize(didItStart, ErrorResponseJsonContext.Default.ErrorResponse);
                                _logger.Log(LogLevel.Information, $"ClientPipeService.StartVPNConnection - writing response to pipe, string is '{startResponseJson}'");
                                ss.WriteString(startResponseJson);
                                _logger.Log(LogLevel.Information, $"ClientPipeService[{threadId}]: Exiting StartVPNConnection command.");
                            }
                            catch (Exception e)
                            {
                                _logger.LogError(e, $"Exception thrown when executing StartVPNConnection and parsing its response. '{e.Message}");
                            }
                            break;
                        case IGuardianNPContract.NPCommands.DisconnectVPNConnection:
                            string entryName = ConnectionRoutines.ActiveConnectionEntryName;
                            _logger.Log(LogLevel.Information, $"ClientPipeService[{threadId}]: Performing DisconnectVPNConnection. Entry is '{entryName}'");
                            var response = cmdDispatcher.DisconnectVPNConnection();
                            var discResponseJson = JsonSerializer.Serialize(response, ErrorResponseJsonContext.Default.ErrorResponse);
                            _logger.Log(LogLevel.Information, $"ClientPipeService.StartVPNConnection - string is '{discResponseJson}'");
                            ss.WriteString(discResponseJson);
                            break;
                        case IGuardianNPContract.NPCommands.GetCurrentVpnConnectionStatus:
                            _logger.Log(LogLevel.Information, $"ClientPipeService[{threadId}]: Performing GetCurrentVpnConnectionStatus");
                            var statusCheck = cmdDispatcher.GetCurrentVpnConnectionStatus();
                            var statusString = JsonSerializer.Serialize(statusCheck, CurrentVPNStatusJsonConect.Default.CurrentVPNStatus);
                            _logger.Log(LogLevel.Information, $"ClientPipeService[{threadId}]: GetCurrentVpnConnectionStatus - writing statusString '{statusString}' to client");
                            ss.WriteString(statusString);
                            break;
                        case IGuardianNPContract.NPCommands.Ping:
                            _logger.Log(LogLevel.Information, $"ClientPipeService[{threadId}]: Performing Ping response to client");
                            ss.WriteString("GFS");
                            break;
                        case IGuardianNPContract.NPCommands.AdministrativeShutdownRequested:
                            _logger.Log(LogLevel.Information, $"ClientPipeService[{threadId}]: Performing AdministrativeShutdownRequested");
                            AdministrativeShutdownRequested = true;
                            break;
                        case IGuardianNPContract.NPCommands.UninstallerShutdownOccurring:
                            _logger.Log(LogLevel.Information, $"ClientPipeService[{threadId}]: Performing UninstallerShutdownOccurring");
                            AdministrativeShutdownRequested = true;
                            var status = cmdDispatcher.GetCurrentVpnConnectionStatus();
                            if (status.ConnectionState == ConnectionStateEnum.Connected)
                            {
                                cmdDispatcher.DisconnectVPNConnection();
                            }
                            break;
                        case IGuardianNPContract.NPCommands.ToggleLogging:
                            _logger.Log(LogLevel.Information, $"ClientPipeService[{threadId}]: Performing ToggleLogging");
                            Common.LogFilterOn = !Common.LogFilterOn;
                            var msg = Common.LogFilterOn ? "ON" : "OFF";
                            _logger.Log(LogLevel.Critical, $"ClientPipeService[{threadId}]: Logging is now turned {msg}");
#if FIXTHIS
                            if (Common.LogFilterOn)
                            {
                                if (cmdPayload.Equals("true", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    Log.CloseAndFlush();
                                    File.Delete(Common.LogFilePath);
                                }
                            }
                            else
                            {
                                Common.SetUpLogging();
                            }
#endif

                            break;
                        case IGuardianNPContract.NPCommands.RequestLogLines:
                            _logger.Log(LogLevel.Information, $"ClientPipeService[{threadId}]: Performing RequestLogLines");
                            int maxLogLines = int.Parse(cmdPayload);
                            var lastLogLines = Common.GetLastLogLines(maxLogLines);
                            _logger.Log(LogLevel.Information, $"ClientPipeService[{threadId}]: Writing log lines to client");
                            var serializedLogLines = JsonSerializer.Serialize<List<string>>(lastLogLines, GuardianConnect.Shared.LogLinesJsonContext.Default.ListString);
                            ss.WriteString(serializedLogLines);
                            break;
                        default:
                            _logger.Log(LogLevel.Information, "WHY ARE WE HERE?");
                            break;
                    }
                }
                // Catch the IOException that is raised if the pipe is broken
                // or disconnected.
                catch (IOException e)
                {
                    //_logger.LogError(e, $"ClientPipeService[{threadId}] IOException ERROR: {0}", e.Message);
                    _logger.Log(LogLevel.Error, $"ClientPipeService[{threadId}]: IOException {e.Message}");
                }
                catch (Exception e)
                {
                    //_logger.LogError(e, "ERROR: {0}", e.Message);
                    _logger.Log(LogLevel.Error, $"ClientPipeService[{threadId}]: Exception {e.Message}");
                }
            }
            _logger.Log(LogLevel.Information, "ClientPipeService.End -- inner While()...");

            Interlocked.Decrement(ref NumberOfClientsConnected);
            pipeServer.Close();
        }
        _logger.Log(LogLevel.Information, "ClientPipeService.End -- outer While()...");
    }
}