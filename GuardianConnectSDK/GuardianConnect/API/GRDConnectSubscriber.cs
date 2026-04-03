using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GuardianConnect.Credentials;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using Serilog;
using static GuardianConnect.Shared.Common;

namespace GuardianConnect.API;

public class GRDConnectSubscriber
{
#pragma warning disable CS0169
    private static Dictionary<string, object>? _deviceDict;
#pragma warning restore CS0169

    public string Identifier { get; set; } = string.Empty;
    
    public string Secret { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string SubscriptionSKU { get; set; } = string.Empty;

    public string SubscriptionNameFormatted { get; set; } = string.Empty;

    public long SubscriptionExpirationDate { get; set; }

    public long CreatedAt { get; set; }

    public GRDConnectDevice? Device { get; set; }

    public bool AcceptedTOS { get; set; }

    // Convenience: Create from dictionary
    public static GRDConnectSubscriber InitFromDictionary(Dictionary<string, JsonElement> subscriberDetailsDict)
    {
        var subscriber = new GRDConnectSubscriber();
        subscriber.Identifier = subscriberDetailsDict[kGuardianConnectSubscriberIdentifier].GetString() ?? "";
        subscriber.Email = subscriberDetailsDict[kGuardianConnectSubscriberEmail].GetString() ?? "";
        subscriber.SubscriptionSKU = subscriberDetailsDict[kGuardianConnectSubscriberSubscriptionSKU].GetString() ?? "";
        subscriber.SubscriptionNameFormatted =
            subscriberDetailsDict[kGuardianConnectSubscriberSubscriptionNameFormatted].GetString() ?? "";
        subscriber.SubscriptionExpirationDate =
            subscriberDetailsDict[kGuardianConnectSubscriberSubscriptionExpirationDate].GetInt64();
        subscriber.CreatedAt = subscriberDetailsDict[kGuardianConnectSubscriberCreatedAt].GetInt64();

        if (subscriberDetailsDict.TryGetValue(kGuardianConnectDeviceDict, out var deviceElement) &&
            deviceElement.ValueKind == JsonValueKind.Object)
        {
            var deviceDict = deviceElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

            if (subscriberDetailsDict.TryGetValue(kGuardianConnectDevicePEToken, out var petTokenElement))
                deviceDict[kGuardianConnectDevicePEToken] = petTokenElement;

            if (subscriberDetailsDict.TryGetValue(kGuardianConnectDevicePETExpires, out var petExpiresElement))
                deviceDict[kGuardianConnectDevicePETExpires] = petExpiresElement;

            subscriber.Device = GRDConnectDevice.InitFromDictionary(deviceDict);
        }

        return subscriber;
    }

    // Retrieve current subscriber from secure storage
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Registry-backed dictionary uses known primitive types safe for AOT")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Registry-backed dictionary uses known primitive types safe for AOT")]
    public static (GRDConnectSubscriber? Subscriber, ErrorResponse errorResponse) GetCurrentSubscriber()
    {
        try
        {
            var retVal = GRDKeychain.ReadDictionaryOfObjects(kGuardianConnectSubscriberStore, out var binaryCSDict);
            if (binaryCSDict.Count == 0 || retVal != 0)
                return (null,
                    new ErrorResponse("Failed to retrieve ConnectSubscriber from registry", IsErrorArg: true));

            var objectDict = new Dictionary<string, JsonElement>();
            objectDict[kGuardianConnectSubscriberIdentifier] =
                JsonSerializer.SerializeToElement(
                    Encoding.UTF8.GetString(binaryCSDict[kGuardianConnectSubscriberIdentifier]));
            objectDict[kGuardianConnectSubscriberSubscriptionSKU] =
                JsonSerializer.SerializeToElement(
                    Encoding.UTF8.GetString(binaryCSDict[kGuardianConnectSubscriberSubscriptionSKU]));
            objectDict[kGuardianConnectSubscriberEmail] =
                JsonSerializer.SerializeToElement(
                    Encoding.UTF8.GetString(binaryCSDict[kGuardianConnectSubscriberEmail]));
            objectDict[kGuardianConnectSubscriberSubscriptionNameFormatted] =
                JsonSerializer.SerializeToElement(
                    Encoding.UTF8.GetString(binaryCSDict[kGuardianConnectSubscriberSubscriptionNameFormatted]));
            objectDict[kGuardianConnectSubscriberCreatedAt] =
                JsonSerializer.SerializeToElement(
                    BitConverter.ToInt64(binaryCSDict[kGuardianConnectSubscriberCreatedAt], 0));
            objectDict[kGuardianConnectSubscriberSubscriptionExpirationDate] =
                JsonSerializer.SerializeToElement(
                    BitConverter.ToInt64(binaryCSDict[kGuardianConnectSubscriberSubscriptionExpirationDate], 0));

            var subscriber = InitFromDictionary(objectDict);
            var mySecret = GRDKeychain.GetPasswordStringForAccount(kGuardianConnectSubscriberSecret);
            subscriber.Secret = mySecret ?? string.Empty;

            var (device, deviceError) = GRDConnectDevice.GetCurrentDevice();
            if (deviceError.IsError)
                return (null, deviceError);

            subscriber.Device = device;
            return (subscriber, new ErrorResponse());
        }
        catch (Exception ex)
        {
            return (null, new ErrorResponse(ex.Message).WithException(ex));
        }
    }

    // Store subscriber securely
    public ErrorResponse Store()
    {
        try
        {
            // Store secret in keychain
            GRDKeychain.StorePassword(Secret, kGuardianConnectSubscriberSecret);

            var dict = new Dictionary<string, byte[]>
            {
                [kGuardianConnectSubscriberIdentifier] = Encoding.UTF8.GetBytes(Identifier),
                [kGuardianConnectSubscriberEmail] = Encoding.UTF8.GetBytes(Email ?? ""),
                [kGuardianConnectSubscriberSubscriptionSKU] = Encoding.UTF8.GetBytes(SubscriptionSKU),
                [kGuardianConnectSubscriberSubscriptionNameFormatted] =
                    Encoding.UTF8.GetBytes(SubscriptionNameFormatted),
                [kGuardianConnectSubscriberSubscriptionExpirationDate] =
                    BitConverter.GetBytes(SubscriptionExpirationDate),
                [kGuardianConnectSubscriberCreatedAt] = BitConverter.GetBytes(CreatedAt),
                [kGuardianConnectDeviceStore] = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }
            };

            GRDKeychain.StoreDictionaryOfObjects(kGuardianConnectSubscriberStore, dict);

            if (Device != null)
            {
                var deviceErr = Device.Store();
                if (!deviceErr.IsError)
                    return deviceErr;
            }

            return new ErrorResponse();
        }
        catch (Exception ex)
        {
            return new ErrorResponse(ex.Message).WithException(ex);
        }
    }

    // Destroy subscriber and related secrets
    public static ErrorResponse DestroySubscriber()
    {
        try
        {
            GRDKeychain.RemoveKeychainItemForAccount(kGuardianConnectSubscriberSecret);
            GRDKeychain.RemoveSubKeyAndValues(kGuardianConnectSubscriberStore);
            GRDKeychain.RemoveSubKeyAndValues(kGuardianConnectDeviceStore);
            var currentPet = GRDPEToken.GetCurrentPEToken();
            currentPet.Remove();

            return new ErrorResponse();
        }
        catch (Exception ex)
        {
            return new ErrorResponse(ex.Message).WithException(ex);
        }
    }

    // List all devices for this subscriber
    // [#167 - calls #193] - DONE
    public async Task<(List<GRDConnectDevice>? Devices, ErrorResponse Error)> AllDevicesAsync()
    {
        try
        {
            var (currentDevice, deviceError) = GRDConnectDevice.GetCurrentDevice();
            if (deviceError.IsError)
                return (null, deviceError);

            var (listOfDevices, response) =
                await GRDHousekeepingAPI.RequestAllConnectDevicesForSubscriberAsync(null, Identifier, Secret);
            //GRDVPNHelper.Singleton.PeToken.Token, Identifier, Secret);
            if (response.IsError)
                return (new List<GRDConnectDevice>(), response);

//                foreach (var deviceObject in listOfDevices)
//                {
//                    //var device = GRDConnectDevice.InitFromDictionary(deviceObject);
//                    var device = deviceObject as GRDConnectDevice;
//                    if (currentDevice != null && device.UUID == currentDevice.UUID)
//                        device.IsCurrentDevice = true;
//                    devices.Add(device);
//                }
            var devices = new List<GRDConnectDevice>();
            foreach (var item in listOfDevices)
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var dict = item.EnumerateObject().ToDictionary(prop => prop.Name, prop => prop.Value);
                var device = GRDConnectDevice.InitFromDictionary(dict);
                if (currentDevice != null && device.UUID == currentDevice.UUID)
                    device.IsCurrentDevice = true;
                devices.Add(device);
            }

            return (devices, new ErrorResponse());
        }
        catch (Exception ex)
        {
            return (null, new ErrorResponse(ex.Message).WithException(ex));
        }
    }

    // Get device reference for current PET [ #168 - calls #186] - TESTED

    [UnconditionalSuppressMessage("Trimming", "IL2026")]
    [UnconditionalSuppressMessage("AOT", "IL3050")]
    public async Task<(GRDConnectDevice? Device, ErrorResponse errorResponse)> ConnectDeviceReferenceAsync()
    {
        try
        {
            var peToken = GRDPEToken.GetCurrentPEToken();
            if (string.IsNullOrEmpty(peToken.Token))
                return (null, new ErrorResponse("No PE-Token present on device"));

            // Let's try to use current device's pe token to obtain device reference
            var currentDevice = GRDConnectDevice.GetCurrentDevice().Device;
            var devicePetoken = currentDevice?.PEToken;

            var (deviceDetails, error) =
                await GRDHousekeepingAPI.GetDeviceReferenceForConnectSubscriberAsync(Identifier, Secret,
                    devicePetoken ?? string.Empty);
//                await GRDHousekeepingAPI.GetDeviceReferenceForConnectSubscriberAsync(Identifier, Secret, peToken.Token);

            if (error.IsError)
                return (null, new ErrorResponse($"Failed to obtain Connect Device reference: {error}"));

            deviceDetails[kGuardianConnectDevicePEToken] = JsonSerializer.SerializeToElement(peToken.Token);
            deviceDetails[kGuardianPETokenExpirationDate] =
                JsonSerializer.SerializeToElement(peToken.ExpirationDateUnix);

            var device = GRDConnectDevice.InitFromDictionary(deviceDetails);
            device.IsCurrentDevice = true;
            return (device, new ErrorResponse());
        }
        catch (Exception ex)
        {
            return (null, ErrorResponse.FromException(ex));
        }
    }

    // Register new subscriber [ #169 - calls #185 ] - DONE ??
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Uses known primitive types safe for AOT")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Uses known primitive types safe for AOT")]
    public async Task<(GRDConnectSubscriber? Subscriber, ErrorResponse)> RegisterNewConnectSubscriberAsync(
        bool acceptedTOS, string deviceNickname, string Identifier, string Secret, string Email)
    {
        if (string.IsNullOrEmpty(Identifier) || string.IsNullOrEmpty(Secret))
            return (null,
                new ErrorResponse(
                    "Unable to register new Connect subscriber. Either the Connect identifier or secret is missing"));

        if (Email == null)
            Email = "";

        var (subscriberDetailsDict, errorResponse) =
            await GRDHousekeepingAPI.AddNewConnectSubscriberAsync(Identifier, Secret, deviceNickname, Email,
                acceptedTOS);
        if (errorResponse.IsError)
            return (null, errorResponse);

        var subscriberDetailsJson = subscriberDetailsDict.ToDictionary(
            kvp => kvp.Key,
            kvp => JsonSerializer.SerializeToElement(kvp.Value));

        if (!subscriberDetailsJson.ContainsKey(kGuardianConnectDevicePEToken))
            return (null, new ErrorResponse("Failed to register new Connect Subscriber. No PE-Token was returned"));

        if (!subscriberDetailsJson.ContainsKey(kGuardianConnectSubscriberEmail))
            subscriberDetailsJson[kGuardianConnectSubscriberEmail] = JsonSerializer.SerializeToElement(Email);

        var newSubscriber = InitFromDictionary(subscriberDetailsJson);

        var petToken = subscriberDetailsJson[kGuardianConnectDevicePEToken].GetString() ?? "";
        var petExpires = subscriberDetailsJson[kGuardianConnectDevicePETExpires].GetInt64();

        var petDict = new Dictionary<string, object>
        {
            [kPETokenKey] = petToken,
            [kGuardianConnectDevicePETExpires] = petExpires
        };

        var petFromConnectSubscriber = GRDPEToken.InitFromDictionary(petDict);
        petFromConnectSubscriber.Store();

        newSubscriber.Secret = Secret;
        newSubscriber.AcceptedTOS = acceptedTOS;

        var createErr = newSubscriber.Store();
        if (createErr.IsError)
            return (null, createErr);

        return (newSubscriber, new ErrorResponse());
    }

    // Check Guardian account setup state
    // [#170 - calls #190] - DONE (? nothing returned from API call so if no GRDApiError set, then all is good)
    public async Task<ErrorResponse> CheckGuardianAccountStateAsync()
    {
        var errorResponse = await GRDHousekeepingAPI.CheckAccountCreationStateAsync(Identifier, Secret);
        return errorResponse;
    }

    // Update subscriber email [ #171 ] - NOT WORKING
    public async Task<(GRDConnectSubscriber? Subscriber, ErrorResponse errorResponse)>
        UpdateConnectSubscriberWithEmailAddressAsync(string email)
    {
        if (string.IsNullOrEmpty(email))
            return (null, new ErrorResponse("New E-Mail is either nil or an empty string. Neither are valid"));
        var currentSubscriber = GetCurrentSubscriber();
        var (currentDevice, deverrorResponse) = GRDConnectDevice.GetCurrentDevice();

        var (subscriberDetails, errorResponse) =
            await GRDHousekeepingAPI.UpdateConnectSubscriberWithEmailAsync(Identifier, Secret,
                currentDevice?.Nickname ?? string.Empty, AcceptedTOS, email);
        if (errorResponse.IsError)
            return (null, errorResponse);

        var subscriber = InitFromDictionary(subscriberDetails);
        subscriber.Secret = Secret;
        var updateErr = subscriber.Store();
        if (updateErr.IsError)
            return (null, updateErr);

        return (subscriber, updateErr);
    }

    // Validate subscriber subscription [ #172 ] - NOT WORKING
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Uses known primitive types safe for AOT")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Uses known primitive types safe for AOT")]
    public async Task<(GRDConnectSubscriber? Subscriber, ErrorResponse errorResponse)> ValidateConnectSubscriberAsync()
    {
        var currentPET = GRDPEToken.GetCurrentPEToken();
        var tokenBefore = currentPET.Token;
        var deviceTokenBefore = GRDConnectDevice.GetCurrentDevice().Device?.PEToken;
        if (string.IsNullOrEmpty(currentPET.Token))
            return (null, new ErrorResponse("Failed to validate Connect subscriber. No PE-Token present on device"));

        var (details, errorResponse) =
            await GRDHousekeepingAPI.ValidateConnectSubscriberAsync(Identifier, Secret, currentPET.Token);

        if (errorResponse.IsError)
            return (null, errorResponse);

        var pet = details[kPETokenKey].GetString();
        if (string.IsNullOrEmpty(pet))
            return (null, new ErrorResponse("Failed to validate Connect Subscriber. No new PE-Token was returned"));

        details[kGuardianConnectSubscriberIdentifier] = JsonSerializer.SerializeToElement(Identifier);
        details[kGuardianConnectSubscriberSecret] = JsonSerializer.SerializeToElement(Secret);
        details[kGuardianConnectSubscriberEmail] = JsonSerializer.SerializeToElement(Email);
        var newSubscriber = InitFromDictionary(details);

        var currentDevice = GRDConnectDevice.GetCurrentDevice().Device;
        currentDevice?.UpdateFromDictionary(details);
        currentDevice?.Store();
        var newDevice = GRDConnectDevice.GetCurrentDevice().Device;
        if (currentDevice?.PEToken != newDevice?.PEToken) Debugger.Break();

        currentPET.UpdateFromDict(details);
        currentPET.Store();

        newSubscriber.Secret = Secret;
        var updateErr = newSubscriber.Store();
        if (updateErr.IsError)
            return (null,
                updateErr.SetErrorMessage(
                    $"Failed to store persistent local data of validated Connect Subscriber: {updateErr.Message}"));

        return (newSubscriber, updateErr);
    }

    // Logout subscriber [ #173 ]
    public async Task<ErrorResponse> LogoutConnectSubscriberAsync()
    {
        var pet = GRDConnectDevice.GetCurrentDevice().Device?.PEToken;
        if (string.IsNullOrEmpty(pet))
            return new ErrorResponse("Failed to validate Connect subscriber. No PE-Token present on device");

        var errorResponse = await GRDHousekeepingAPI.LogOutConnectSubscriberAsync(pet);
        return errorResponse;
    }
}