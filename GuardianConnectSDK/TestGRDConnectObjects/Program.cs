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
    Log.Error($"testconfig.json not found at {configPath}");
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

Log.Information($"Config loaded — host={config.ConnectApiHostname}, identifier={config.SubscriberIdentifier}");

// ── SDK bootstrap ──────────────────────────────────────────────────────────────
GRDVPNHelper.CreateSingleton();
GRDVPNHelper.Singleton.ConnectAPIHostname = config.ConnectApiHostname;

// ── Working state ──────────────────────────────────────────────────────────────
GRDConnectSubscriber? subscriber    = null;
GRDConnectDevice?     device        = null;
GRDConnectDevice?     currentDevice = null;
GRDConnectDevice?     alternateDevice = null;

// ═══════════════════════════════════════════════════════════════════════════════
// GetCurrentSubscriber / RegisterNewConnectSubscriberAsync / CheckGuardianAccountStateAsync
// ═══════════════════════════════════════════════════════════════════════════════
Header("GetCurrentSubscriber / RegisterNewConnectSubscriberAsync / CheckGuardianAccountStateAsync");

(subscriber, var getErr) = GRDConnectSubscriber.GetCurrentSubscriber();
if (getErr.IsError)
{
    Log.Warning($"No stored subscriber ({getErr.Message}). Registering new subscriber...");

    if (string.IsNullOrEmpty(config.SubscriberIdentifier) || string.IsNullOrEmpty(config.SubscriberSecret))
    {
        Log.Error("No stored subscriber and testconfig.json has no identifier/secret — cannot proceed. Stopping.");
        PressEnter();
        return;
    }

    var regSub = new GRDConnectSubscriber
    {
        Identifier = config.SubscriberIdentifier,
        Secret     = config.SubscriberSecret,
        Email      = config.SubscriberEmail ?? ""
    };

    (subscriber, var regErr) = await regSub.RegisterNewConnectSubscriberAsync(
        config.AcceptedTOS, config.DeviceNickname);
    PrintResult(regErr, $"Registered — Identifier={subscriber?.Identifier}, CreatedAt={subscriber?.CreatedAt}",
        $"Identifier={config.SubscriberIdentifier}, Email={config.SubscriberEmail}, DeviceNickname={config.DeviceNickname}, AcceptedTOS={config.AcceptedTOS}");

    if (regErr.IsError)
    {
        PressEnter();
        return;
    }

    if (subscriber?.Device != null)
    {
        device = subscriber.Device;
        Log.Information($"Device from registration — UUID={device.UUID}, Nickname={device.Nickname}");
    }
}
else
{
    Log.Information($"Stored subscriber loaded — Identifier={subscriber!.Identifier}, SKU={subscriber.SubscriptionSKU}, Secret={subscriber.Secret}, PEToken={subscriber.Device?.PEToken}");

    if (subscriber.Device != null)
    {
        device = subscriber.Device;
        Log.Information($"Device loaded from registry — UUID={device.UUID}, Nickname={device.Nickname}");
    }
}

// CheckGuardianAccountStateAsync runs in both paths (stored or newly registered)
if (!string.IsNullOrEmpty(config.SubscriberSecret))
    subscriber!.Secret = config.SubscriberSecret;
var accountStateErr = await subscriber!.CheckGuardianAccountStateAsync();
PrintResult(accountStateErr, "Account state confirmed on backend",
    $"Identifier={subscriber.Identifier}, SecretSet={!string.IsNullOrEmpty(subscriber.Secret)}");

// ═══════════════════════════════════════════════════════════════════════════════
// GetCurrentSubscriber / RegisterNewConnectSubscriberAsync
// ═══════════════════════════════════════════════════════════════════════════════
Header("GetCurrentSubscriber / RegisterNewConnectSubscriberAsync");

(subscriber, var step2Err) = GRDConnectSubscriber.GetCurrentSubscriber();
if (step2Err.IsError)
{
    Log.Warning($"Subscriber not in registry ({step2Err.Message}). Registering...");

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
        Log.Information($"Device from registration — UUID={device.UUID}, Nickname={device.Nickname}");
    }
}
else
{
    Log.Information($"Subscriber confirmed in registry — Identifier={subscriber!.Identifier}, SKU={subscriber.SubscriptionSKU}");

    if (subscriber.Device != null)
    {
        device = subscriber.Device;
        Log.Information($"Device confirmed in registry — UUID={device.UUID}, Nickname={device.Nickname}");
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// ConnectDeviceReferenceAsync  (pre-validate)
// ═══════════════════════════════════════════════════════════════════════════════
Header("ConnectDeviceReferenceAsync (pre-validate)");

var preStoredPet = GRDPEToken.GetCurrentPEToken().Token;
(device, var preDevRefErr) = await subscriber!.ConnectDeviceReferenceAsync();
PrintResult(preDevRefErr, $"Device reference — UUID={device?.UUID}, Nickname={device?.Nickname}",
    $"Identifier={subscriber.Identifier}, Device PEToken={device?.PEToken}, Stored PEToken={preStoredPet}");

// ═══════════════════════════════════════════════════════════════════════════════
// AllDevicesAsync  (pre-validate)
// ═══════════════════════════════════════════════════════════════════════════════
Header("AllDevicesAsync (pre-validate)");

(var preAllDevices, var preAllDevErr) = await subscriber.AllDevicesAsync();
PrintResult(preAllDevErr, $"AllDevicesAsync returned {preAllDevices?.Count ?? 0} device(s)",
    $"Identifier={subscriber.Identifier}, SecretSet={!string.IsNullOrEmpty(subscriber.Secret)}");
if (preAllDevices != null && !preAllDevErr.IsError)
{
    foreach (var d in preAllDevices)
        Log.Information($"  Device — UUID={d.UUID}, Nickname={d.Nickname}, IsCurrent={d.IsCurrentDevice}");

    foreach (var d in preAllDevices)
    {
        if (d.IsCurrentDevice)
        {
            Log.Information($"  Skipping current device — UUID={d.UUID}, Nickname={d.Nickname}");
            continue;
        }
        Log.Information($"  Deleting device — UUID={d.UUID}, Nickname={d.Nickname}");
        var delErr = await d.DeleteDeviceAsync(preStoredPet, subscriber.Identifier, subscriber.Secret);
        PrintResult(delErr, $"Deleted device UUID={d.UUID}, Nickname={d.Nickname}",
            $"PEToken={preStoredPet}, Identifier={subscriber.Identifier}");
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// GRDConnectDevice.GetCurrentDevice  (pre-validate)
// ═══════════════════════════════════════════════════════════════════════════════
Header("GRDConnectDevice.GetCurrentDevice (pre-validate)");

(currentDevice, var preGetDevErr) = GRDConnectDevice.GetCurrentDevice();
PrintResult(preGetDevErr, $"Current device — UUID={currentDevice?.UUID}, Nickname={currentDevice?.Nickname}, PETExpires={currentDevice?.PETExpires}",
    "(reads from registry — no call parameters)");

// ═══════════════════════════════════════════════════════════════════════════════
// GRDConnectDevice.ListConnectDevicesForPETokenAsync  (pre-validate)
// ═══════════════════════════════════════════════════════════════════════════════
Header("GRDConnectDevice.ListConnectDevicesForPETokenAsync (pre-validate)");

var preDevPet = currentDevice?.PEToken;
if (!string.IsNullOrEmpty(preDevPet))
{
    (var preListDevices, var preListErr) = await GRDConnectDevice.ListConnectDevicesForPETokenAsync(preDevPet);
    if (preListErr.IsError)
        Log.Error($"FAIL — {preListErr}  |  Inputs: PEToken={preDevPet}");
    else
    {
        Log.Information($"OK   — {preListDevices?.Count ?? 0} device(s) returned");
        if (preListDevices != null)
            foreach (var d in preListDevices)
            {
                Log.Information($"  Device — UUID={d.UUID}, Nickname={d.Nickname}, IsCurrent={d.IsCurrentDevice}");
                if (!d.IsCurrentDevice) alternateDevice = d;
            }
    }
}
else
{
    Log.Warning("Skipping ListConnectDevicesForPETokenAsync — no PE-Token on current device");
}

// ═══════════════════════════════════════════════════════════════════════════════
// GRDConnectDevice.ValidateConnectDeviceAsync  (pre-validate)
// ═══════════════════════════════════════════════════════════════════════════════
Header("GRDConnectDevice.ValidateConnectDeviceAsync (pre-validate) - SKIPPING");

#if false
if (currentDevice != null && !string.IsNullOrEmpty(currentDevice.PEToken))
{
    (var preValidatedDevice, var preDevValErr) = await currentDevice.ValidateConnectDeviceAsync(currentDevice.PEToken);
    PrintResult(preDevValErr, $"Device validated — UUID={preValidatedDevice?.UUID}",
        $"PEToken={currentDevice.PEToken}, DeviceUUID={currentDevice.UUID}");
}
else
{
    Log.Warning("Skipping ValidateConnectDeviceAsync — no current device or missing PEToken");
}
#endif

// ═══════════════════════════════════════════════════════════════════════════════
// ValidateConnectSubscriberAsync
// ═══════════════════════════════════════════════════════════════════════════════
Header("ValidateConnectSubscriberAsync");

var validateIdentifier = subscriber.Identifier;
(subscriber, var validateErr) = await subscriber.ValidateConnectSubscriberAsync();
// Re-apply secret — Store() clears it and InitFromDictionary does not restore it
if (subscriber != null && !string.IsNullOrEmpty(config.SubscriberSecret))
    subscriber.Secret = config.SubscriberSecret;
PrintResult(validateErr,
    $"Subscriber validated — SKU={subscriber?.SubscriptionSKU}, Expires={DateTimeOffset.FromUnixTimeSeconds(subscriber!.SubscriptionExpirationDate)}",
    $"Identifier={validateIdentifier}, AcceptedTOS={config.AcceptedTOS}");

if (validateErr != null && validateErr.IsError)
{
    PressEnter();
    return;
}

// ═══════════════════════════════════════════════════════════════════════════════
// GRDConnectDevice.ValidateConnectDeviceAsync  (post-validate)
// ═══════════════════════════════════════════════════════════════════════════════
Header("GRDConnectDevice.ValidateConnectDeviceAsync (post-validate - SKIPPING)");

#if false
(currentDevice, var postGetDevErr) = GRDConnectDevice.GetCurrentDevice();
if (currentDevice != null && !string.IsNullOrEmpty(currentDevice.PEToken))
{
    (var postValidatedDevice, var postDevValErr) = await currentDevice.ValidateConnectDeviceAsync(currentDevice.PEToken);
    PrintResult(postDevValErr, $"Device validated — UUID={postValidatedDevice?.UUID}",
        $"PEToken={currentDevice.PEToken}, DeviceUUID={currentDevice.UUID}");
}
else
{
    Log.Warning("Skipping ValidateConnectDeviceAsync — no current device or missing PEToken");
}
#endif

// ═══════════════════════════════════════════════════════════════════════════════
// ConnectDeviceReferenceAsync  (post-validate)
// ═══════════════════════════════════════════════════════════════════════════════
Header("ConnectDeviceReferenceAsync (post-validate)");

var postStoredPet = GRDPEToken.GetCurrentPEToken().Token;
(device, var postDevRefErr) = await subscriber.ConnectDeviceReferenceAsync();
PrintResult(postDevRefErr, $"Device reference — UUID={device?.UUID}, Nickname={device?.Nickname}",
    $"Identifier={subscriber.Identifier}, Device PEToken={device?.PEToken}, Stored PEToken={postStoredPet}");

// ═══════════════════════════════════════════════════════════════════════════════
// AllDevicesAsync  (post-validate)
// ═══════════════════════════════════════════════════════════════════════════════
Header("GRDConnectSubscriber.AllDevicesAsync (post-validate)");

(var postAllDevices, var postAllDevErr) = await subscriber.AllDevicesAsync();
PrintResult(postAllDevErr, $"AllDevicesAsync returned {postAllDevices?.Count ?? 0} device(s)",
    $"Identifier={subscriber.Identifier}, SecretSet={!string.IsNullOrEmpty(subscriber.Secret)}");
if (postAllDevices != null)
    foreach (var d in postAllDevices)
        Log.Information($"  Device — UUID={d.UUID}, Nickname={d.Nickname}, IsCurrent={d.IsCurrentDevice}");

// ═══════════════════════════════════════════════════════════════════════════════
// GRDConnectDevice.ListConnectDevicesForPETokenAsync  (post-validate)
// ═══════════════════════════════════════════════════════════════════════════════
Header("GRDConnectDevice.ListConnectDevicesForPETokenAsync (post-validate)");

var postDevPet = currentDevice?.PEToken;
if (!string.IsNullOrEmpty(postDevPet))
{
    (var postListDevices, var postListErr) = await GRDConnectDevice.ListConnectDevicesForPETokenAsync(postDevPet);
    if (postListErr.IsError)
        Log.Error($"FAIL — {postListErr}  |  Inputs: PEToken={postDevPet}");
    else
    {
        Log.Information($"OK   — {postListDevices?.Count ?? 0} device(s) returned");
        if (postListDevices != null)
        {
            foreach (var d in postListDevices)
            {
                Log.Information($"  Device — UUID={d.UUID}, Nickname={d.Nickname}, IsCurrent={d.IsCurrentDevice}");
                if (!d.IsCurrentDevice) alternateDevice = d;
            }

            // Delete the added (non-current) devices, passing each device's PEToken
            foreach (var d in postListDevices)
            {
                if (d.IsCurrentDevice)
                {
                    Log.Information($"  Skipping current device — UUID={d.UUID}, Nickname={d.Nickname}");
                    continue;
                }
                var delPet = d.PEToken ?? postDevPet;
                Log.Information($"  Deleting added device — UUID={d.UUID}, Nickname={d.Nickname}");
                var delErr = await d.DeleteDeviceAsync(delPet, subscriber.Identifier, subscriber.Secret);
                PrintResult(delErr, $"Deleted device UUID={d.UUID}, Nickname={d.Nickname}",
                    $"PEToken={delPet}, Identifier={subscriber.Identifier}");
            }
        }
    }
}
else
{
    Log.Warning("Skipping ListConnectDevicesForPETokenAsync — no PE-Token on current device");
}

// ═══════════════════════════════════════════════════════════════════════════════
// GRDConnectDevice.AddConnectDeviceAsync  (set 'countOfAdditionalDevicesToCreate' > 0)
// ═══════════════════════════════════════════════════════════════════════════════
Header("GRDConnectDevice.AddConnectDeviceAsync");

var addDevicePet = GRDPEToken.GetCurrentPEToken().Token;
var baseNickname = device?.Nickname ?? config.DeviceNickname;

if (config.CountOfAdditionalDevicesToCreate > 0)
{
    if (string.IsNullOrEmpty(addDevicePet))
    {
        Log.Warning("Skipping AddConnectDeviceAsync — no current PE-Token");
    }
    else
    {
        for (int i = 2; i <= config.CountOfAdditionalDevicesToCreate + 1; i++)
        {
            var extraNickname = $"{baseNickname}-Extra-{i}";
            Log.Information($"Creating extra device {i} with nickname '{extraNickname}'");
            (var extraDevice, var addErr) =
                await GRDConnectDevice.AddConnectDeviceAsync(addDevicePet, extraNickname, config.AcceptedTOS);
            PrintResult(addErr, $"Extra device {i} created — UUID={extraDevice?.UUID}, Nickname={extraDevice?.Nickname}",
                $"PEToken={addDevicePet}, Nickname={extraNickname}, AcceptedTOS={config.AcceptedTOS}");
        }
    }
}
else
{
    Log.Information("Skipping — set 'countOfAdditionalDevicesToCreate' > 0 in testconfig.json to enable");
}

// ═══════════════════════════════════════════════════════════════════════════════
// GRDConnectSubscriber.AllDevicesAsync + GRDConnectDevice.DeleteDeviceAsync
// (runs after add — lists all devices via subscriber, deletes each via identifier/secret)
// ═══════════════════════════════════════════════════════════════════════════════
Header("GRDConnectSubscriber.AllDevicesAsync + DeleteDeviceAsync (post-add, via identifier/secret)");

if (config.CountOfAdditionalDevicesToCreate > 0 && !string.IsNullOrEmpty(addDevicePet))
{
    (var postAddAllDevices, var postAddAllDevErr) = await subscriber.AllDevicesAsync();
    PrintResult(postAddAllDevErr, $"AllDevicesAsync returned {postAddAllDevices?.Count ?? 0} device(s)",
        $"Identifier={subscriber.Identifier}, SecretSet={!string.IsNullOrEmpty(subscriber.Secret)}");

    if (postAddAllDevices != null && !postAddAllDevErr.IsError)
    {
        foreach (var d in postAddAllDevices)
        {
            if (d.IsCurrentDevice)
            {
                Log.Information($"  Skipping current device — UUID={d.UUID}, Nickname={d.Nickname}");
                continue;
            }
            Log.Information($"  Deleting device — UUID={d.UUID}, Nickname={d.Nickname}");
            var delErr = await d.DeleteDeviceAsync(addDevicePet, subscriber.Identifier, subscriber.Secret);
            PrintResult(delErr, $"Deleted device UUID={d.UUID}, Nickname={d.Nickname}",
                $"PEToken={addDevicePet}, Identifier={subscriber.Identifier}");
        }
    }
}
else
{
    Log.Information("Skipping — no additional devices were created");
}

// ═══════════════════════════════════════════════════════════════════════════════
// GRDConnectDevice.UpdateConnectDeviceNicknameAsync  (set 'newDeviceNickname' in testconfig.json to enable)
// ═══════════════════════════════════════════════════════════════════════════════
Header("GRDConnectDevice.UpdateConnectDeviceNicknameAsync");

if (alternateDevice != null && currentDevice != null
    && !string.IsNullOrEmpty(currentDevice.PEToken)
    && !string.IsNullOrEmpty(config.NewDeviceNickname))
{
    (var updatedDevice, var updateDevErr) =
        await alternateDevice.UpdateConnectDeviceNicknameAsync(currentDevice.PEToken, config.NewDeviceNickname);
    PrintResult(updateDevErr, $"Nickname updated — '{updatedDevice?.Nickname}'",
        $"PEToken={currentDevice.PEToken}, DeviceUUID={alternateDevice.UUID}, NewNickname={config.NewDeviceNickname}");
}
else
{
    Log.Information("Skipping — set 'newDeviceNickname' in testconfig.json to enable");
}

// ═══════════════════════════════════════════════════════════════════════════════
// UpdateConnectSubscriberWithEmailAddressAsync  (set 'newEmail' in testconfig.json to enable)
// ═══════════════════════════════════════════════════════════════════════════════
Header("UpdateConnectSubscriberWithEmailAddressAsync");

if (!string.IsNullOrEmpty(config.NewEmail))
{
    (var updatedSubscriber, var updateEmailErr) =
        await subscriber.UpdateConnectSubscriberWithEmailAddressAsync(config.NewEmail);
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
// LogoutConnectSubscriberAsync  (set 'runLogout: true' in testconfig.json to enable)
// ═══════════════════════════════════════════════════════════════════════════════
Header("LogoutConnectSubscriberAsync");

if (config.RunLogout)
{
    var logoutErr = await subscriber.LogoutConnectSubscriberAsync();
    PrintResult(logoutErr, "Subscriber logged out",
        $"Identifier={subscriber.Identifier}");
}
else
{
    Log.Information("Skipping — set 'runLogout: true' in testconfig.json to enable");
}

// ═══════════════════════════════════════════════════════════════════════════════
// DestroySubscriber  (set 'runDestroy: true' in testconfig.json to enable)
// ═══════════════════════════════════════════════════════════════════════════════
Header("DestroySubscriber");

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
    Log.Information($"{title}");
    Log.Information("══════════════════════════════════════════════════════");
}

void PrintResult(ErrorResponse? err, string successMessage, string? inputs = null)
{
    if (err != null && err.IsError)
    {
        if (inputs != null)
            Log.Error($"       Inputs: {inputs}");
        var apiErr = err.GRDApiError as GRDAPIError;
        if (apiErr != null)
            Log.Error($"FAIL — {err.Message}  |  ApiError [{apiErr.StatusCode}] {apiErr.Title}: {apiErr.Message}");
        else if (err.GRDApiError != null)
            Log.Error($"FAIL — {err.Message}  |  GRDApiError (raw): {err.GRDApiError}");
        else
            Log.Error($"FAIL — {err.Message}  |  (no GRDApiError)");
    }
    else
    {
        Log.Information($"OK   — {successMessage}");
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
    public string  ConnectApiHostname            { get; set; } = "connect-api.guardianapp.com";
    public string  SubscriberIdentifier          { get; set; } = "";
    public string  SubscriberSecret              { get; set; } = "";
    public string? SubscriberEmail               { get; set; }
    public string  DeviceNickname                { get; set; } = "TestDevice";
    public bool    AcceptedTOS                   { get; set; } = true;
    public int     CountOfAdditionalDevicesToCreate { get; set; } = 0;
    public string? NewEmail                      { get; set; }
    public string? NewDeviceNickname             { get; set; }
    public bool    RunLogout                     { get; set; } = false;
    public bool    RunDestroy                    { get; set; } = false;
}
