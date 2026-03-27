using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using GuardianConnect.API.Model;
using GuardianConnect.Credentials;
using GuardianConnect.Shared;
using static GuardianConnect.Shared.Common;

namespace GuardianConnect.API;

public class GRDConnectDevice
{
    // Properties
    public string Nickname { get; set; } = string.Empty;
    public string UUID { get; set; } = string.Empty;
    public string? PEToken { get; set; }
    public long PETExpires { get; set; }
    public long CreatedAt { get; set; }
    public bool IsCurrentDevice { get; set; }

    // Convenience method to create from dictionary
    public static GRDConnectDevice InitFromDictionary(IDictionary<string, JsonElement> deviceDictionary)
    {
        var device = new GRDConnectDevice();

        device.Nickname = deviceDictionary[kGuardianConnectDeviceNickname].GetString() ?? "";
        device.UUID = deviceDictionary[kGuardianConnectDeviceUUID].GetString() ?? "";

        if (deviceDictionary.TryGetValue(kGuardianConnectDevicePEToken, out var petTokenEl) &&
            petTokenEl.ValueKind is JsonValueKind.String or JsonValueKind.Null)
            device.PEToken = petTokenEl.GetString();

        if (deviceDictionary.TryGetValue(kGuardianConnectDevicePETExpires, out var petExpiresEl) &&
            petExpiresEl.ValueKind == JsonValueKind.Number)
            device.PETExpires = petExpiresEl.GetInt64();

        device.CreatedAt = deviceDictionary[kGuardianConnectDeviceCreatedAt].GetInt64();

        if (deviceDictionary.TryGetValue("CurrentDevice", out var currentDeviceEl))
            device.IsCurrentDevice = currentDeviceEl.GetBoolean();
        else if (deviceDictionary.TryGetValue("currentDevice", out var lowerCurrentDeviceEl))
            device.IsCurrentDevice = lowerCurrentDeviceEl.GetBoolean();

        return device;
    }

    public void UpdateFromDictionary(IDictionary<string, JsonElement> deviceDictionary)
    {
        if (deviceDictionary == null)
            throw new ArgumentNullException(nameof(deviceDictionary), "Device dictionary cannot be null");

        if (deviceDictionary.Count == 0)
            throw new ArgumentException("Device dictionary cannot be empty", nameof(deviceDictionary));
        if (deviceDictionary.ContainsKey(kPETokenKey))
            PEToken = deviceDictionary[kPETokenKey].GetString();
        if (deviceDictionary.ContainsKey(kGuardianConnectDevicePETExpires))
            PETExpires = deviceDictionary[kGuardianConnectDevicePETExpires].GetInt64();
    }


    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Registry-backed dictionary uses known primitive types safe for AOT")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Registry-backed dictionary uses known primitive types safe for AOT")]
    public static (GRDConnectDevice? Device, ErrorResponse Error) GetCurrentDevice()
    {
        try
        {
            var retVal = GRDKeychain.ReadDictionaryOfObjects(kGuardianConnectDeviceStore, out var binaryDict);
            if (binaryDict.Count == 0 || retVal != 0)
                return (null, new ErrorResponse("Failed to retrieve ConnectDevice from registry", IsErrorArg: true));

            var objectDict = new Dictionary<string, JsonElement>();
            objectDict[kGuardianConnectDeviceNickname] =
                JsonSerializer.SerializeToElement(Encoding.UTF8.GetString(binaryDict[kGuardianConnectDeviceNickname]));
            objectDict[kGuardianConnectDeviceUUID] =
                JsonSerializer.SerializeToElement(Encoding.UTF8.GetString(binaryDict[kGuardianConnectDeviceUUID]));
            objectDict[kGuardianConnectDeviceCreatedAt] =
                JsonSerializer.SerializeToElement(
                    long.Parse(Encoding.UTF8.GetString(binaryDict[kGuardianConnectDeviceCreatedAt])));
            objectDict[kGuardianConnectDevicePEToken] =
                JsonSerializer.SerializeToElement(Encoding.UTF8.GetString(binaryDict[kGuardianConnectDevicePEToken]));
            objectDict[kGuardianConnectDevicePETExpires] =
                JsonSerializer.SerializeToElement(
                    long.Parse(Encoding.UTF8.GetString(binaryDict[kGuardianConnectDevicePETExpires])));
            objectDict["CurrentDevice"] =
                JsonSerializer.SerializeToElement(bool.Parse(Encoding.UTF8.GetString(binaryDict["CurrentDevice"])));

            var device = InitFromDictionary(objectDict);
            return (device, new ErrorResponse());
        }
        catch (Exception ex)
        {
            return (null, new ErrorResponse(ex.Message).WithException(ex));
        }
    }

    public ErrorResponse Store()
    {
        try
        {
            var deviceDict = new Dictionary<string, byte[]>
            {
                [kGuardianConnectDeviceNickname] = Encoding.UTF8.GetBytes(Nickname),
                [kGuardianConnectDeviceUUID] = Encoding.UTF8.GetBytes(UUID),
                [kGuardianConnectDevicePEToken] = Encoding.UTF8.GetBytes(PEToken ?? ""),
                [kGuardianConnectDeviceCreatedAt] = Encoding.UTF8.GetBytes(CreatedAt.ToString()),
                [kGuardianConnectDevicePETExpires] = Encoding.UTF8.GetBytes(PETExpires.ToString()),
                ["CurrentDevice"] = Encoding.UTF8.GetBytes(IsCurrentDevice.ToString())
            };
            GRDKeychain.StoreDictionaryOfObjects(kGuardianConnectDeviceStore, deviceDict);

            // TJE - per CJ talk - going to replicate storing GRDPEToken here if given in the device so we stay consistent
            if (!string.IsNullOrEmpty(PEToken))
            {
                var updatedPet = GRDPEToken.GetCurrentPEToken();
                updatedPet.Token = PEToken;
                updatedPet.ExpirationDateUnix = PETExpires;
                updatedPet.ExpirationDate = DateTimeOffset.FromUnixTimeSeconds(PETExpires).DateTime;
                updatedPet.Store();
            }

            return new ErrorResponse();
        }
        catch (Exception ex)
        {
            return new ErrorResponse(ex.Message).WithException(ex);
        }
    }

    // Destroy current device
    public static ErrorResponse Destroy()
    {
        try
        {
            GRDKeychain.RemoveSubKeyAndValues(kGuardianConnectDeviceStore);
            return new ErrorResponse();
        }
        catch (Exception ex)
        {
            return ErrorResponse.FromException(ex);
        }
    }

    // API Wrappers

    // Register a new device
    // [#179 - calls #191]
    public static async Task<(GRDConnectDevice? Device, ErrorResponse Error)>
        AddConnectDeviceAsync(string peToken, string nickname, bool acceptedTOS)
    {
        try
        {
            var ConnectDeviceRequest = ConnectDeviceRequestData.ForNickName(peToken, nickname);
            ConnectDeviceRequest.AcceptedTOS = acceptedTOS.ToString();
            var (deviceDict, response) = await GRDHousekeepingAPI.AddConnectDeviceAsync(peToken, nickname, acceptedTOS);
            if (response.IsError)
                return (null, new ErrorResponse(response.Message));

            if (deviceDict == null)
                return (null, new ErrorResponse("Invalid device data from API"));

            var device = InitFromDictionary(deviceDict);
            return (device, new ErrorResponse());
        }
        catch (Exception ex)
        {
            return (null, new ErrorResponse(ex.Message).WithException(ex));
        }
    }


    // Update device nickname
    // [#180 - calls #192]
    public async Task<(GRDConnectDevice? Device, ErrorResponse)> UpdateConnectDeviceNicknameAsync(string peToken,
        string newNickname)
    {
        try
        {
            var updateRequest = ConnectDeviceRequestData.ForNickName(peToken, newNickname);
            var (deviceDict, errorResponse) =
                await GRDHousekeepingAPI.UpdateConnectDeviceAsync(peToken, newNickname, UUID);
            if (errorResponse.IsError)
                return (null, errorResponse);

            if (deviceDict == null)
                return (null, new ErrorResponse("Invalid device data from API"));

            var device = InitFromDictionary(deviceDict);
            return (device, new ErrorResponse());
        }
        catch (Exception ex)
        {
            return (null, new ErrorResponse(ex.Message).WithException(ex));
        }
    }

    // List devices for PEToken
    // [#181 - calls #193] (#167 also calls #193)

    public static async Task<(List<GRDConnectDevice>? Devices, ErrorResponse errorResponse)>
        ListConnectDevicesForPETokenAsync(string peToken)
    {
        var (currentDevice, deviceError) = GetCurrentDevice();
        if (deviceError.IsError)
            return (null, deviceError);

        try
        {
            var (listOfDevices, errorResponse) =
                await GRDHousekeepingAPI.RequestAllConnectDevicesForSubscriberAsync(peToken);
            if (errorResponse.IsError)
                return (null, errorResponse);

            var devices = new List<GRDConnectDevice>();
            foreach (var item in listOfDevices)
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var dict = item.EnumerateObject().ToDictionary(kvp => kvp.Name, kvp => kvp.Value);
                var device = InitFromDictionary(dict);
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

    // [#178 - calls #194] Delete device
    // TJE - CHECK THIS - Delete a singular device from the registry as the only one?
    public async Task<ErrorResponse> DeleteDeviceAsync(string peToken, string identifier, string secret)
    {
        try
        {
            var errorResponse = await GRDHousekeepingAPI.DeleteConnectDeviceAsync(peToken, identifier, secret);
            return errorResponse;
        }
        catch (Exception ex)
        {
            return new ErrorResponse(ex.Message).WithException(ex);
        }
    }


    // [#183 - calls #195]
    public async Task<(GRDConnectDevice? Device, ErrorResponse)> ValidateConnectDeviceAsync(string peToken)
    {
        try
        {
            var (dict, response) = await GRDHousekeepingAPI.ValidateConnectDeviceAsync(peToken);
            if (response.IsError)
                return (null, new ErrorResponse(response.Message));

            if (response.Data is not Dictionary<string, JsonElement> deviceDict)
            {
                var errorResponse =
                    new ErrorResponse("Invalid device data from API");
                errorResponse = response;
                errorResponse.HttpResponse = response.HttpResponse;
                return (null, errorResponse);
            }

            var device = InitFromDictionary(deviceDict);
            return (device, new ErrorResponse());
        }
        catch (Exception ex)
        {
            return (null, new ErrorResponse(ex.Message).WithException(ex));
        }
    }
}