using System.Text.Json;
using GuardianConnect.API;
using GuardianConnect.Credentials;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

// ── Logging setup ──────────────────────────────────────────────────────────────
Serilog.Log.Logger = new Serilog.LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

var serviceCollection = new ServiceCollection();
serviceCollection.AddLogging(builder =>
{
    builder.AddSerilog();
    builder.SetMinimumLevel(LogLevel.Debug);
});
var serviceProvider = serviceCollection.BuildServiceProvider();
var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
StaticLoggerFactory.Initialize(loggerFactory);

// ── Load testconfig.json ───────────────────────────────────────────────────────
var configPath = Path.Combine(AppContext.BaseDirectory, "testconfig.json");
if (!File.Exists(configPath))
{
    Log.Error("testconfig.json not found at {Path}", configPath);
    PressEnter();
    return;
}

TestConfig config;
try
{
    var json = File.ReadAllText(configPath);
    config = JsonSerializer.Deserialize<TestConfig>(json,
                 new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
             ?? throw new InvalidOperationException("Deserialization returned null");
}
catch (Exception ex)
{
    Log.Error(ex, "Failed to parse testconfig.json");
    PressEnter();
    return;
}

Log.Information("Config loaded — host={Host}, identifier={Id}",
    config.ConnectApiHostname, config.SubscriberIdentifier);

// ── SDK bootstrap ──────────────────────────────────────────────────────────────
GRDVPNHelper.CreateSingleton();
GRDVPNHelper.Singleton.ConnectAPIHostname = config.ConnectApiHostname;

// ── Working state ──────────────────────────────────────────────────────────────
GRDConnectSubscriber? subscriber = null;
GRDConnectDevice?     device     = null;

// ═══════════════════════════════════════════════════════════════════════════════
// STEP 1 – GetCurrentSubscriber  →  CheckGuardianAccountStateAsync  →  RegisterNewConnectSubscriberAsync
// ═══════════════════════════════════════════════════════════════════════════════
Header("STEP 1 – GetCurrentSubscriber / CheckGuardianAccountStateAsync / RegisterNewConnectSubscriberAsync");

(subscriber, var getErr) = GRDConnectSubscriber.GetCurrentSubscriber();
if (getErr.IsError)
{
    Log.Warning("No stored subscriber ({Msg}). Checking account state on backend...", getErr.Message);

    if (string.IsNullOrEmpty(config.SubscriberIdentifier) || string.IsNullOrEmpty(config.SubscriberSecret))
    {
        Log.Error("No stored subscriber and testconfig.json has no identifier/secret — cannot proceed. Stopping.");
        PressEnter();
        return;
    }

    // Build a stub subscriber from config so we can call instance methods
    var configSub = new GRDConnectSubscriber
    {
        Identifier = config.SubscriberIdentifier,
        Secret     = config.SubscriberSecret,
        Email      = config.SubscriberEmail ?? ""
    };

    var accountStateErr = await configSub.CheckGuardianAccountStateAsync();
    PrintResult(accountStateErr, "Account exists on backend — will validate in Step 2",
        $"Identifier={config.SubscriberIdentifier}, SecretSet={!string.IsNullOrEmpty(config.SubscriberSecret)}");

    if (!accountStateErr.IsError)
    {
        // Account already exists — adopt configSub and let Step 2 validate it
        subscriber = configSub;
    }
    else
    {
        // Account not found — register fresh
        Log.Information("Account not found on backend — registering new subscriber...");
        (subscriber, var regErr) = await configSub.RegisterNewConnectSubscriberAsync(
            config.AcceptedTOS, config.DeviceNickname);
        PrintResult(regErr, $"Registered — Identifier={subscriber?.Identifier}, CreatedAt={subscriber?.CreatedAt}",
            $"Identifier={config.SubscriberIdentifier}, Email={config.SubscriberEmail}, DeviceNickname={config.DeviceNickname}, AcceptedTOS={config.AcceptedTOS}");

        if (regErr.IsError)
        {
            PressEnter();
            return;
        }

        // Registration may have returned a device in the response — capture it
        if (subscriber?.Device != null)
        {
            device = subscriber.Device;
            Log.Information("Device from registration — UUID={UUID}, Nickname={Nick}", device.UUID, device.Nickname);
        }
    }
}
else
{
    Log.Information("Stored subscriber loaded — Identifier={Id}, SKU={SKU}, Secret={Secret}, PEToken={PEToken}",
        subscriber!.Identifier, subscriber.SubscriptionSKU, subscriber.Secret, subscriber.Device.PEToken);

    // GetCurrentSubscriber also loaded the device from the registry — capture it now
    if (subscriber.Device != null)
    {
        device = subscriber.Device;
        Log.Information("Device loaded from registry — UUID={UUID}, Nickname={Nick}", device.UUID, device.Nickname);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// STEP 2 – GetCurrentSubscriber / RegisterNewConnectSubscriberAsync
// ═══════════════════════════════════════════════════════════════════════════════
Header("STEP 2 – GetCurrentSubscriber / RegisterNewConnectSubscriberAsync");

(subscriber, var step2Err) = GRDConnectSubscriber.GetCurrentSubscriber();
if (step2Err.IsError)
{
    Log.Warning("Subscriber not in registry ({Msg}). Registering...", step2Err.Message);

    if (string.IsNullOrEmpty(config.SubscriberIdentifier) || string.IsNullOrEmpty(config.SubscriberSecret))
    {
        Log.Error("No identifier/secret in config — cannot register. Stopping.");
        PressEnter();
        return;
    }

    var regSub2 = new GRDConnectSubscriber
    {
        Identifier = config.SubscriberIdentifier,
        Secret     = config.SubscriberSecret,
        Email      = config.SubscriberEmail ?? ""
    };

    (subscriber, var regErr2) = await regSub2.RegisterNewConnectSubscriberAsync(
        config.AcceptedTOS, config.DeviceNickname);
    PrintResult(regErr2, $"Registered — Identifier={subscriber?.Identifier}, CreatedAt={subscriber?.CreatedAt}",
        $"Identifier={config.SubscriberIdentifier}, Email={config.SubscriberEmail}, DeviceNickname={config.DeviceNickname}, AcceptedTOS={config.AcceptedTOS}");

    if (regErr2.IsError)
    {
        PressEnter();
        return;
    }

    if (subscriber?.Device != null)
    {
        device = subscriber.Device;
        Log.Information("Device from registration — UUID={UUID}, Nickname={Nick}", device.UUID, device.Nickname);
    }
}
else
{
    Log.Information("Subscriber confirmed in registry — Identifier={Id}, SKU={SKU}",
        subscriber!.Identifier, subscriber.SubscriptionSKU);

    if (subscriber.Device != null)
    {
        device = subscriber.Device;
        Log.Information("Device confirmed in registry — UUID={UUID}, Nickname={Nick}", device.UUID, device.Nickname);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// STEP 3 – ConnectDeviceReferenceAsync
// ═══════════════════════════════════════════════════════════════════════════════
Header("STEP 3 – ConnectDeviceReferenceAsync");

///var step3PET = GRDPEToken.GetCurrentPEToken().Token;
(device, var devRefErr) = await subscriber!.ConnectDeviceReferenceAsync();
PrintResult(devRefErr, $"Device reference — UUID={device?.UUID}, Nickname={device?.Nickname}",
    $"Identifier={subscriber.Identifier}, PEToken={subscriber.Device.PEToken}");

// ═══════════════════════════════════════════════════════════════════════════════
// STEP 4 – GRDConnectDevice.AddConnectDeviceAsync  (set 'countOfAdditionalDevicesToCreate' > 0)
// ═══════════════════════════════════════════════════════════════════════════════
Header("STEP 4 – GRDConnectDevice.AddConnectDeviceAsync (additional devices)");

var addDevicePET = GRDPEToken.GetCurrentPEToken().Token;
var baseNickname = device?.Nickname ?? config.DeviceNickname;

if (config.CountOfAdditionalDevicesToCreate > 0)
{
    if (string.IsNullOrEmpty(addDevicePET))
    {
        Log.Warning("Skipping AddConnectDeviceAsync — no current PE-Token");
    }
    else
    {
        for (int i = 2; i <= config.CountOfAdditionalDevicesToCreate + 1; i++)
        {
            var extraNickname = $"{baseNickname}-Extra-{i}";
            Log.Information("Creating extra device {N} with nickname '{Nickname}'", i, extraNickname);
            (var extraDevice, var addErr) =
                await GRDConnectDevice.AddConnectDeviceAsync(addDevicePET, extraNickname, config.AcceptedTOS);
            PrintResult(addErr, $"Extra device {i} created — UUID={extraDevice?.UUID}, Nickname={extraDevice?.Nickname}",
                $"PEToken={addDevicePET}, Nickname={extraNickname}, AcceptedTOS={config.AcceptedTOS}");
        }
    }
}
else
{
    Log.Information("Skipping — set 'countOfAdditionalDevicesToCreate' > 0 in testconfig.json to enable");
}

// ═══════════════════════════════════════════════════════════════════════════════
// STEP 5 – AllDevicesAsync
// ═══════════════════════════════════════════════════════════════════════════════
Header("STEP 5 – AllDevicesAsync");

(var allDevices, var allDevErr) = await subscriber!.AllDevicesAsync();
PrintResult(allDevErr, $"AllDevicesAsync returned {allDevices?.Count ?? 0} device(s)",
    $"Identifier={subscriber.Identifier}, SecretSet={!string.IsNullOrEmpty(subscriber.Secret)}");
if (allDevices != null)
    foreach (var d in allDevices)
        Log.Information("  Device — UUID={UUID}, Nickname={Nick}, IsCurrent={Current}",
            d.UUID, d.Nickname, d.IsCurrentDevice);

// ═══════════════════════════════════════════════════════════════════════════════
// STEP 6 – GRDConnectDevice.GetCurrentDevice
// ═══════════════════════════════════════════════════════════════════════════════
Header("STEP 6 – GRDConnectDevice.GetCurrentDevice");

(var currentDevice, var currentDevErr) = GRDConnectDevice.GetCurrentDevice();
PrintResult(currentDevErr, $"Current device — UUID={currentDevice?.UUID}, Nickname={currentDevice?.Nickname}, PETExpires={currentDevice?.PETExpires}",
    "(reads from registry — no call parameters)");

// ═══════════════════════════════════════════════════════════════════════════════
// STEP 7 – ValidateConnectDeviceAsync
// ═══════════════════════════════════════════════════════════════════════════════
Header("STEP 7 – ValidateConnectDeviceAsync");

if (currentDevice != null && !string.IsNullOrEmpty(currentDevice.PEToken))
{
    (var validatedDevice, var devValErr) = await currentDevice.ValidateConnectDeviceAsync(currentDevice.PEToken!);
    PrintResult(devValErr, $"Device validated — UUID={validatedDevice?.UUID}",
        $"PEToken={currentDevice.PEToken}, DeviceUUID={currentDevice.UUID}");
}
else
{
    Log.Warning("Skipping ValidateConnectDeviceAsync — no current device or missing PEToken");
}

// ═══════════════════════════════════════════════════════════════════════════════
// STEP 8 – GRDConnectDevice.ListConnectDevicesForPETokenAsync
// ═══════════════════════════════════════════════════════════════════════════════
Header("STEP 8 – GRDConnectDevice.ListConnectDevicesForPETokenAsync");

var currentPET = GRDPEToken.GetCurrentPEToken().Token;
if (!string.IsNullOrEmpty(currentPET))
{
    (var petDevices, var petDevErrMsg) = await GRDConnectDevice.ListConnectDevicesForPETokenAsync(currentPET);
    if (petDevErrMsg.IsError)
        Log.Error("FAIL — {Err}  |  Inputs: PEToken={PET}", petDevErrMsg, currentPET);
    else
    {
        Log.Information("OK   — {Count} device(s) returned", petDevices?.Count ?? 0);
        if (petDevices != null)
            foreach (var d in petDevices)
                Log.Information("  Device — UUID={UUID}, Nickname={Nick}, IsCurrent={Current}",
                    "d.UUID", "d.Nickname", "d.IsCurrentDevice");
    }
}
else
{
    Log.Warning("Skipping ListConnectDevicesForPETokenAsync — no current PE-Token");
}

// ═══════════════════════════════════════════════════════════════════════════════
// STEP 9 – UpdateConnectDeviceNicknameAsync  (set 'newDeviceNickname' in testconfig.json to enable)
// ═══════════════════════════════════════════════════════════════════════════════
Header("STEP 9 – UpdateConnectDeviceNicknameAsync");

#if SKIPPING
if (currentDevice != null
    && !string.IsNullOrEmpty(currentDevice.PEToken)
    && !string.IsNullOrEmpty(config.NewDeviceNickname))
{
    (var updatedDevice, var updateDevErr) =
        await currentDevice.UpdateConnectDeviceNicknameAsync(currentDevice.PEToken!, config.NewDeviceNickname);
    PrintResult(updateDevErr, $"Nickname updated — '{updatedDevice?.Nickname}'",
        $"PEToken={currentDevice.PEToken}, DeviceUUID={currentDevice.UUID}, NewNickname={config.NewDeviceNickname}");
}
else
#endif
{
    Log.Information("Skipping — set 'newDeviceNickname' in testconfig.json to enable");
}

// ═══════════════════════════════════════════════════════════════════════════════
// STEP 10 – UpdateConnectSubscriberWithEmailAddressAsync  (set 'newEmail' in testconfig.json to enable)
// ═══════════════════════════════════════════════════════════════════════════════
Header("STEP 10 – UpdateConnectSubscriberWithEmailAddressAsync");

if (!string.IsNullOrEmpty(config.NewEmail))
{
    (var updatedSubscriber, var updateEmailErr) =
        await subscriber!.UpdateConnectSubscriberWithEmailAddressAsync(config.NewEmail);
    PrintResult(updateEmailErr, $"Email updated — '{updatedSubscriber?.Email}'",
        $"Identifier={subscriber.Identifier}, NewEmail={config.NewEmail}");
    if (updatedSubscriber != null)
        subscriber = updatedSubscriber;
}
else
{
    Log.Information("Skipping — set 'newEmail' in testconfig.json to enable");
}

// ═══════════════════════════════════════════════════════════════════════════════
// OPTIONAL – LogoutConnectSubscriberAsync  (set 'runLogout: true' in testconfig.json to enable)
// ═══════════════════════════════════════════════════════════════════════════════
Header("OPTIONAL – LogoutConnectSubscriberAsync");

if (config.RunLogout)
{
    var logoutErr = await subscriber!.LogoutConnectSubscriberAsync();
    PrintResult(logoutErr, "Subscriber logged out",
        $"Identifier={subscriber.Identifier}");
}
else
{
    Log.Information("Skipping — set 'runLogout: true' in testconfig.json to enable");
}

// ═══════════════════════════════════════════════════════════════════════════════
// OPTIONAL – DestroySubscriber  (set 'runDestroy: true' in testconfig.json to enable)
// ═══════════════════════════════════════════════════════════════════════════════
Header("OPTIONAL – DestroySubscriber");

if (config.RunDestroy)
{
    var destroyErr = await GRDConnectSubscriber.DestroySubscriber();
    PrintResult(destroyErr, "Subscriber destroyed");
}
else
{
    Log.Information("Skipping — set 'runDestroy: true' in testconfig.json to enable");
}

PressEnter();

// ── Helpers ────────────────────────────────────────────────────────────────────

void Header(string title)
{
    Log.Information("");
    Log.Information("══════════════════════════════════════════════════════");
    Log.Information("{Title}", title);
    Log.Information("══════════════════════════════════════════════════════");
}

void PrintResult(ErrorResponse? err, string successMessage, string? inputs = null)
{
    if (err != null && err.IsError)
    {
        if (inputs != null)
            Log.Error("       Inputs: {Inputs}", inputs);
        var apiErr = err.GRDApiError as GRDAPIError;
        if (apiErr != null)
            Log.Error("FAIL — {Msg}  |  ApiError [{Status}] {Title}: {ApiMsg}",
                err.Message, apiErr.StatusCode, apiErr.Title, apiErr.Message);
        else if (err.GRDApiError != null)
            Log.Error("FAIL — {Msg}  |  GRDApiError (raw): {Raw}", err.Message, err.GRDApiError.ToString());
        else
            Log.Error("FAIL — {Msg}  |  (no GRDApiError)", err.Message);
    }
    else
    {
        Log.Information("OK   — {Msg}", successMessage);
    }
}

void PressEnter()
{
    Console.Write("\nPress ENTER to exit...");
    Console.ReadLine();
}

// ── Config model ───────────────────────────────────────────────────────────────

public class TestConfig
{
    public string  ConnectApiHostname  { get; set; } = "connect-api.guardianapp.com";
    public string  SubscriberIdentifier { get; set; } = "";
    public string  SubscriberSecret     { get; set; } = "";
    public string? SubscriberEmail      { get; set; }
    public string  DeviceNickname                  { get; set; } = "TestDevice";
    public bool    AcceptedTOS                     { get; set; } = true;
    public int     CountOfAdditionalDevicesToCreate { get; set; } = 0;
    public string? NewEmail             { get; set; }
    public string? NewDeviceNickname    { get; set; }
    public bool    RunLogout            { get; set; } = false;
    public bool    RunDestroy           { get; set; } = false;
}
