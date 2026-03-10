using System.Globalization;
using System.Text;
using GuardianConnect.API.Model;
using GuardianConnect.Credentials;
using GuardianConnect.API.Model;
using GuardianConnect.Shared;

namespace GuardianConnect.API
{
    public class GRDConnectDevice
    {
        // Properties
        public string Nickname { get; set; }
        public string UUID { get; set; }
        public string? PEToken { get; set; }
        public long PETExpires { get; set; }
        public long CreatedAt { get; set; }
        public bool IsCurrentDevice { get; set; }

        // Convenience method to create from dictionary
        public static GRDConnectDevice InitFromDictionary(IDictionary<string, object> deviceDictionary)
        {
            var device = new GRDConnectDevice();
            device.Nickname = (string)deviceDictionary[Common.kGuardianConnectDeviceNicknameKey];
            device.UUID = (string)deviceDictionary[Common.kGuardianConnectDeviceUUIDKey];
            device.PEToken = (string)deviceDictionary[Common.kGuardianConnectDevicePETokenKey];
            device.PETExpires = long.Parse(deviceDictionary[Common.kGuardianConnectDevicePETExpiresKey].ToString() ?? "0");
            device.CreatedAt = long.Parse(deviceDictionary[Common.kGuardianConnectDeviceCreatedAtKey].ToString() ?? "0");
            device.IsCurrentDevice = deviceDictionary.TryGetValue("currentDevice", out var currentDevice) &&
                                     currentDevice is bool b && b;

            return device;
        }

        // Async method to get current device (simulate completion block)
        public static async Task<(GRDConnectDevice? Device, ErrorResponse Error)> GetCurrentDeviceAsync()
        {
            try
            {
                int retVal = GRDKeychain.ReadDictionaryOfObjects(Common.kGuardianConnectDeviceStoreKey, out var binaryDict);
                if (binaryDict.Count == 0 || retVal != 0)
                    return (null, new ErrorResponse("Failed to retrieve ConnectDevice from registry", IsErrorArg:true));

                var objectDict = new Dictionary<string, object>();
                objectDict.Add(Common.kGuardianConnectDeviceNicknameKey, Encoding.UTF8.GetString(binaryDict[Common.kGuardianConnectDeviceNicknameKey]));
                objectDict.Add(Common.kGuardianConnectDeviceUUIDKey, Encoding.UTF8.GetString(binaryDict[Common.kGuardianConnectDeviceUUIDKey]));
                objectDict.Add(Common.kGuardianConnectDeviceCreatedAtKey, Encoding.UTF8.GetString(binaryDict[Common.kGuardianConnectDeviceCreatedAtKey]));
                objectDict.Add(Common.kGuardianConnectDevicePETokenKey, Encoding.UTF8.GetString(binaryDict[Common.kGuardianConnectDevicePETokenKey]));
                objectDict.Add(Common.kGuardianConnectDevicePETExpiresKey, Encoding.UTF8.GetString(binaryDict[Common.kGuardianConnectDevicePETExpiresKey]));
                
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
                    [Common.kGuardianConnectDeviceNicknameKey] = Encoding.UTF8.GetBytes(Nickname),
                    [Common.kGuardianConnectDeviceUUIDKey] = Encoding.UTF8.GetBytes(UUID),
                    [Common.kGuardianConnectDevicePETokenKey] = Encoding.UTF8.GetBytes(PEToken ?? ""),
                    [Common.kGuardianConnectDeviceCreatedAtKey] = Encoding.UTF8.GetBytes(CreatedAt.ToString()),
                    [Common.kGuardianConnectDevicePETExpiresKey] = Encoding.UTF8.GetBytes(PETExpires.ToString()),
                    ["CurrentDevice"] = Encoding.UTF8.GetBytes(IsCurrentDevice.ToString())
                };
                GRDKeychain.StoreDictionaryOfObjects(Common.kGuardianConnectDeviceStoreKey, deviceDict);
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
                GRDKeychain.RemoveSubKeyAndValues(Common.kGuardianConnectDeviceStoreKey);
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
        public async Task<(GRDConnectDevice? Device, ErrorResponse)> UpdateConnectDeviceNicknameAsync(string peToken, string newNickname)
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
        public static async Task<(List<GRDConnectDevice>? Devices, string? Error)> ListConnectDevicesForPETokenAsync(string peToken)
        {
            // CHECK - if/when take GRDVPNHelper PEToken instead of parameter of this call
            try
            {
                var (deviceDictsList, errorResponse) =
                    await GRDHousekeepingAPI.RequestAllConnectDevicesForSubscriberAsync(peToken);
                if (errorResponse.IsError)
                    return (null, errorResponse.Message);
                
                var deviceList = deviceDictsList.Select(InitFromDictionary).ToList();

                var currentDevice = await GetCurrentDeviceAsync();
                if (currentDevice.Device != null)
                {
                    deviceList.Find(device => device.UUID == currentDevice.Device.UUID).IsCurrentDevice = true;
                }
                return (deviceList, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }


        // Delete device
        // TJE - CHECK THIS - Delete a singular device from the registry as the only one?
        public async Task<ErrorResponse> DeleteDeviceAsync()
        {
            try
            {
                var retVal = GRDKeychain.RemoveSubKeyAndValues(Common.kGuardianConnectDeviceStoreKey);
                return retVal == 0
                    ? new ErrorResponse()
                    : new ErrorResponse("Failed to remove ConnectDevice from registry");
            }
            catch (Exception ex)
            {
                return new ErrorResponse(ex.Message).WithException(ex);
            }
        }

 
        // [#183 - calls #195]
        public async Task<(GRDConnectDevice? Device, ErrorResponse)> ValidateConnectDeviceAsync(string peToken)
        {
            // TJE: TODO
            try
            {
                var response = await GRDHousekeepingAPI.ValidateConnectDeviceAsync(peToken);
                if (response.IsError)
                    return (null, new ErrorResponse(response.Message));

                var deviceDict = response.Data as Dictionary<string, object>;
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
    }
}