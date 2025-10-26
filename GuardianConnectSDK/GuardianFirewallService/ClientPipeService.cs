using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Serilog;
using GuardianConnect.Shared;
using System.Text.Json.Serialization;
using Win32Calls;

namespace GuardianFirewallService;

public class ClientPipeService : BackgroundService
{
    private static int numThreads = 4;
    private static Thread?[] servers = new Thread[numThreads];
    private static CancellationToken _cancellationToken;
    private static bool AdministrativeShutdownRequested = false;

    internal static int NumberOfClientsConnected;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _cancellationToken = stoppingToken;
        _cancellationToken.ThrowIfCancellationRequested();
        Log.Information("ClientPipeService: TESTING LOG");
        Log.Information("ClientPipeService running at: {time}", DateTimeOffset.Now);

        stoppingToken.Register(() => Log.Information("ClientPipeService is stopping."));
        
        StartServerListeners();

        try
        {
            var heartbeatCounter = 0;
            var priorMessage =
                $"ClientPipeService is running... Clients connected: {NumberOfClientsConnected}. Cancellation Request is {stoppingToken.IsCancellationRequested}";
            Log.Information(
                $"Going into while() loop. stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
            while (!stoppingToken.IsCancellationRequested)
            {
                var currentMessage =
                    $"ClientPipeService is running... Clients connected: {NumberOfClientsConnected}. Cancellation Request is {stoppingToken.IsCancellationRequested}";

                if (!currentMessage.Equals(priorMessage))
                {
                    Log.Information(currentMessage);
                    heartbeatCounter = 0;
                    priorMessage = currentMessage;
                }
                else if (++heartbeatCounter % 5 == 0) Log.Information("ClientPipeService is running...");

                await Task.Delay(60000, stoppingToken);
            }

            Log.Information(
                $"Past while() loop. stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");

            StopServerListenerThreads();
        }
        catch (OperationCanceledException oce) when (!oce.CancellationToken.IsCancellationRequested)
        {
            Log.Information("OperationCanceledException");
            // When the stopping token is canceled, for example, a call made from services.msc,
            // we shouldn't exit with a non-zero exit code. In other words, this is expected...

            Log.Error(oce, "{Message}", oce.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Message}", ex.Message);

            // Terminates this process and returns an exit code to the operating system.
            // This is required to avoid the 'BackgroundServiceExceptionBehavior', which
            // performs one of two scenarios:
            // 1. When set to "Ignore": will do nothing at all, errors cause zombie services.
            // 2. When set to "StopHost": will cleanly stop the host, and log errors.
            //
            // In order for the Windows Service Management system to leverage configured
            // recovery options, we need to terminate the process with a non-zero exit code.
        }
        Log.Information("ClientPipeService: past Task Creation clause...");
    }

    public override Task? ExecuteTask { get; }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        System.Diagnostics.StackTrace t = new System.Diagnostics.StackTrace();
        //Log.Information($"StartAsync StackTrace: \n{t}");
        Log.Information("StartAsync():");

        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        //System.Diagnostics.StackTrace t = new System.Diagnostics.StackTrace();
        //Log.Information("ClientPipeService.StopAsync StackTrace: \n{t}");
        Log.Information("ClientPipeService.StopAsync called.");

        return base.StopAsync(cancellationToken);
    }

    public void StartServerListeners()
    {
        int i;

        Log.Information("\n*** Named pipe server stream ***\n");
        Log.Information("Waiting for client connect...\n");
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
                        Log.Information("Server thread[{0}] finished.", servers[j]!.ManagedThreadId);
                        servers[j] = null;
                        i--; // decrement the thread watch count
                    }
                }
            }
        }

        Log.Information("\nServer threads exhausted, exiting.");

    }

    public void ServerThread(object? data)
    {
        GuardianNPCommandDispatcher cmdDispatcher = new GuardianNPCommandDispatcher();

        int threadId = Thread.CurrentThread.ManagedThreadId;

        while (!_cancellationToken.IsCancellationRequested && !AdministrativeShutdownRequested)
        {
            PipeSecurity pipeSecurity = new PipeSecurity();
            pipeSecurity.AddAccessRule(
                new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), PipeAccessRights.FullControl,
                    AccessControlType.Allow));

            //NamedPipeServerStream pipeServer = new NamedPipeServerStream("GuardianFirewallService", PipeDirection.InOut, numThreads);
            NamedPipeServerStream pipeServer = NamedPipeServerStreamAcl.Create("GuardianFirewallService",
                PipeDirection.InOut, 8, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                65536, 65536, pipeSecurity);

            // Wait for a client to connect
            Log.Information($"Pipe Service Thread #{threadId} going to wait for Client Connection...");
            pipeServer.WaitForConnection();

            Interlocked.Increment(ref NumberOfClientsConnected);
            Log.Information("Client connected on thread[{0}].", threadId);
            StreamString ss = new StreamString(pipeServer);
            // Verify our identity to the connected client using a
            // string that the client anticipates.
            ss.WriteString($"GuardianFirewallService#ACK#{PowerTransitionHandler.ConnectedAtSuspendTime()}");

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
                    Log.Information($"ClientPipeService[{threadId}]: Waiting for command from client ...");
                    string commandString = ss.ReadStringAsync().GetAwaiter().GetResult();
                    Log.Information($"ClientPipeService[{threadId}]: string from client: {commandString}");
                    if (!pipeServer.IsConnected) continue;
                    char cmdToken = commandString[0];
                    string cmdPayload = commandString.Substring(2); // Skip the '.' between first char cmd enum and params data
                    IGuardianNPContract.NPCommands cmd = (IGuardianNPContract.NPCommands)(Int16.Parse(cmdToken.ToString()));

                    Log.Information($"ClientPipeService[{threadId}]: Cmd={cmd}, payload='{cmdPayload}");
                    switch (cmd)
                    {
                        case IGuardianNPContract.NPCommands.StartVPNConnection:
                            Log.Information($"ClientPipeService[{threadId}]: Performing StartVPNConnection");
                            var serializedVpnParameters = cmdPayload;
                            var vpnCallParameters = JsonSerializer.Deserialize<VPNCallParameters>(serializedVpnParameters, VPNCallParametersJsonContext.Default.VPNCallParameters);
                            var didItStart = cmdDispatcher.StartVPNConnection(vpnCallParameters);
                            Log.Information($"ClientPipeService[{threadId}]: Exiting StartVPNConnection");
                            var startResponseJson = JsonSerializer.Serialize(didItStart, ErrorResponseJsonContext.Default.ErrorResponse);
                            Log.Information($"ClientPipeService.StartVPNConnection - string is '{startResponseJson}'");
                            ss.WriteString(startResponseJson);
                            break;
                        case IGuardianNPContract.NPCommands.DisconnectVPNConnection:
                            string entryName = ConnectionRoutines.ActiveConnectionEntryName;
                            Log.Information($"ClientPipeService[{threadId}]: Performing DisconnectVPNConnection. Entry is '{entryName}'");
                            cmdDispatcher.DisconnectVPNConnection();
                            break;
                        case IGuardianNPContract.NPCommands.GetCurrentVpnConnectionStatus:
                            Log.Information($"ClientPipeService[{threadId}]: Performing GetCurrentVpnConnectionStatus");
                            var statusCheck = cmdDispatcher.GetCurrentVpnConnectionStatus();
                            var statusString = JsonSerializer.Serialize(statusCheck, CurrentVPNStatusJsonConect.Default.CurrentVPNStatus);
                            Log.Information($"ClientPipeService[{threadId}]: GetCurrentVpnConnectionStatus - writing statusString '{statusString}' to client");
                            ss.WriteString(statusString);
                            break;
                        case IGuardianNPContract.NPCommands.Ping:
                            Log.Information($"ClientPipeService[{threadId}]: Performing Ping response to client");
                            ss.WriteString("GFS");
                            break;
                        case IGuardianNPContract.NPCommands.AdministrativeShutdownRequested:
                            Log.Information($"ClientPipeService[{threadId}]: Performing AdministrativeShutdownRequested");
                            AdministrativeShutdownRequested = true;
                            break;
                        case IGuardianNPContract.NPCommands.UninstallerShutdownOccurring:
                            Log.Information($"ClientPipeService[{threadId}]: Performing UninstallerShutdownOccurring");
                            AdministrativeShutdownRequested = true;
                            var status = cmdDispatcher.GetCurrentVpnConnectionStatus();
                            if (status.ConnectionState == ConnectionStateEnum.Connected)
                            {
                                cmdDispatcher.DisconnectVPNConnection();
                            }
                            break;
                        case IGuardianNPContract.NPCommands.ToggleLogging:
                            Log.Information($"ClientPipeService[{threadId}]: Performing ToggleLogging");
                            Common.LogFilterOn = !Common.LogFilterOn;
                            var msg = Common.LogFilterOn ? "ON" : "OFF";
                            Log.Information($"ClientPipeService[{threadId}]: Logging is now turned {msg}");
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

                            break;
                        case IGuardianNPContract.NPCommands.RequestLogLines:
                            Log.Information($"ClientPipeService[{threadId}]: Performing RequestLogLines");
                            int maxLogLines = int.Parse(cmdPayload);
                            var lastLogLines = Common.GetLastLogLines(maxLogLines);
                            string serializedLogs = JsonSerializer.Serialize(lastLogLines, JsonSerializerOptions.Default);
                            Log.Information($"ClientPipeService[{threadId}]: Writing log lines to client");
                            ss.WriteString(serializedLogs);
                            break;
                        default:
                            Log.Information("WHY ARE WE HERE?");
                            break;
                    }
                }
                // Catch the IOException that is raised if the pipe is broken
                // or disconnected.
                catch (IOException e)
                {
                    Log.Error(e, $"ClientPipeService[{threadId}] IOException ERROR: {0}", e.Message);
                }
                catch (Exception e)
                {
                    Log.Error(e, "ERROR: {0}", e.Message);
                }
            }
            Log.Information("ClientPipeService.End -- inner While()...");

            Interlocked.Decrement(ref NumberOfClientsConnected);
            pipeServer.Close();
        }
        Log.Information("ClientPipeService.End -- outer While()...");
    }
}