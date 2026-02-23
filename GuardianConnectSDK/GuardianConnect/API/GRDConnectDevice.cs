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
        // Constants
        public const string kGuardianConnectDeviceKey = "ep-grd-device";
        public const string kGuardianConnectDeviceNicknameKey = "ep-grd-device-nickname";
        public const string kGuardianConnectDeviceUUIDKey = "ep-grd-device-uuid";
        public const string kGuardianConnectDeviceCreatedAtKey = "ep-grd-device-created-at";
        public const string kGuardianConnectDevicePETokenKey = "pe-token";
        public const string kGuardianConnectDevicePETExpiresKey = "pet-expires";
        public const string kGuardianConnectDevice = "kGuardianConnectDevice";

        // Properties
        public string Nickname { get; set; }
        public string UUID { get; set; }
        public string? PEToken { get; set; }
        public DateTime? PETExpires { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsCurrentDevice { get; set; }

        // Convenience method to create from dictionary
        public static GRDConnectDevice InitFromDictionary(IDictionary<string, object> deviceDictionary)
        {
            var device = new GRDConnectDevice
            {
                Nickname = (string)deviceDictionary[kGuardianConnectDeviceNicknameKey],
                UUID = (string)deviceDictionary[kGuardianConnectDeviceUUIDKey],
                PEToken = (string)deviceDictionary[kGuardianConnectDevicePETokenKey],
                PETExpires = DateTime.Parse((string)deviceDictionary[kGuardianConnectDevicePETExpiresKey], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CreatedAt = DateTime.Parse((string)deviceDictionary[kGuardianConnectDeviceCreatedAtKey], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                IsCurrentDevice = deviceDictionary.TryGetValue("currentDevice", out var currentDevice) && currentDevice is bool b && b
            };

            return device;
        }

        // Async method to get current device (simulate completion block)
        public static async Task<(GRDConnectDevice? Device, ErrorResponse Error)> GetCurrentDeviceAsync()
        {
            try
            {
                int retVal = GRDKeychain.ReadDictionaryOfObjects(kGuardianConnectDevice, out var binaryDict);
                if (binaryDict == null || binaryDict.Count == 0 || retVal != 0)
                    return (null, new ErrorResponse("Failed to retrieve ConnectDevice from registry", IsErrorArg:true));

                var objectDict = new Dictionary<string, object>();
                objectDict.Add(kGuardianConnectDeviceNicknameKey, Encoding.UTF8.GetString(binaryDict[kGuardianConnectDeviceNicknameKey]));
                objectDict.Add(kGuardianConnectDeviceUUIDKey, Encoding.UTF8.GetString(binaryDict[kGuardianConnectDeviceUUIDKey]));
                objectDict.Add(kGuardianConnectDeviceCreatedAtKey, Encoding.UTF8.GetString(binaryDict[kGuardianConnectDeviceCreatedAtKey]));
                objectDict.Add(kGuardianConnectDevicePETokenKey, Encoding.UTF8.GetString(binaryDict[kGuardianConnectDevicePETokenKey]));
                objectDict.Add(kGuardianConnectDevicePETExpiresKey, DateTime.Parse(Encoding.UTF8.GetString(binaryDict[kGuardianConnectDevicePETExpiresKey])));
                
                var device = InitFromDictionary(objectDict);
                return (device, new ErrorResponse());
            }
            catch (Exception ex)
            {
                return (null, new ErrorResponse(ex.Message).WithException(ex));
            }
        }

        public async Task<ErrorResponse> StoreAsync()
        {
            try
            {
                var deviceDict = new Dictionary<string, byte[]>
                {
                    [kGuardianConnectDeviceNicknameKey] = Encoding.UTF8.GetBytes(Nickname),
                    [kGuardianConnectDeviceUUIDKey] = Encoding.UTF8.GetBytes(UUID),
                    [kGuardianConnectDevicePETokenKey] = Encoding.UTF8.GetBytes(PEToken ?? ""),
                    [kGuardianConnectDeviceCreatedAtKey] = Encoding.UTF8.GetBytes(CreatedAt.ToString("O")),
                    [kGuardianConnectDevicePETExpiresKey] = PETExpires.HasValue
                        ? Encoding.UTF8.GetBytes(PETExpires.Value.ToString("O"))
                        : new byte[0],
                    ["currentDevice"] = Encoding.UTF8.GetBytes(IsCurrentDevice.ToString())
                };
                GRDKeychain.StoreDictionaryOfObjects(kGuardianConnectDeviceKey, deviceDict);
                return new ErrorResponse();
            }
            catch (Exception ex)
            {
                return new ErrorResponse(ex.Message).WithException(ex);
            }
        }

        // Destroy current device
        public static async Task<string?> DestroyAsync()
        {
            try
            {
                GRDKeychain.RemoveSubKeyAndValues(kGuardianConnectDeviceKey);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }


        // API Wrappers

        // Register a new device
        public static async Task<(GRDConnectDevice? Device, ErrorResponse Error)>
            AddConnectDeviceAsync(string peToken, string nickname, bool acceptedTOS)
        {
            try
            {
                var ConnectDeviceRequest = ConnectDeviceRequestData.ForNickName(peToken, nickname);
                ConnectDeviceRequest.AcceptedTOS = acceptedTOS.ToString();
                var response = await GRDHousekeepingAPI.CallHostToAddConnectDeviceAsync(ConnectDeviceRequest);
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


        // Update device nickname
        public async Task<(GRDConnectDevice? Device, ErrorResponse)> UpdateConnectDeviceNicknameAsync(string peToken, string newNickname)
        {
            try
            {
                var updateRequest = ConnectDeviceRequestData.ForNickName(peToken, newNickname);
                var response = await GRDHousekeepingAPI.CallHostToUpdateConnectDeviceAsync(updateRequest);
                if (response.IsError)
                    return (null, response);

                var device = response.Data as GRDConnectDevice;
                if (device == null)
                    return (null, new ErrorResponse("Invalid device data from API"));
                return (device, new ErrorResponse());
            }
            catch (Exception ex)
            {
                return (null, new ErrorResponse(ex.Message).WithException(ex));
            }
        }

        // List devices for PEToken
        public static async Task<(List<GRDConnectDevice>? Devices, string? Error)> ListConnectDevicesForPETokenAsync(string peToken)
        {
            try
            {
                ConnectDeviceRequestData request = ConnectDeviceRequestData.WithPeToken(peToken);
                var (devices, errorResponse) = await GRDHousekeepingAPI.RequestAllConnectDevicesForSubscriberAsync(request);
                if (errorResponse.IsError)
                    return (null, errorResponse.Message);

                var deviceList = devices;
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
                var retVal = GRDKeychain.RemoveKeychainItemForAccount(kGuardianConnectDeviceKey);
                return retVal == 0
                    ? new ErrorResponse()
                    : new ErrorResponse("Failed to remove ConnectDevice from registry");
            }
            catch (Exception ex)
            {
                return new ErrorResponse(ex.Message).WithException(ex);
            }
        }

        // Validate device PEToken
        /// <summary>
        /// /Add a function called validateConnectDeviceWithDevicePEToken which accepts the function parameter
        /// peToken type: string
        /// The function should call the API endpoint /api/v1.2/partners/subscriber/device/validate and parse the response
        /// data into a GRDConnectDevice object by calling the initFromDictionary function. It should finally return the
        /// parse connect device object
        /// </summary>
        /// <param name="peToken"></param>
        /// <returns></returns>
        public async Task<(GRDConnectDevice? Device, ErrorResponse)> ValidateConnectDeviceAsync(string peToken)
        {
            // TJE: TODO
            try
            {
                var response = await GRDHousekeepingAPI.CallHostToValidateConnectDeviceAsync(peToken);
                if (response.IsError)
                    return (null, new ErrorResponse(response.Message));

                var deviceDict = response.Data as Dictionary<string, object>;
                if (deviceDict == null)
                    return (null, new ErrorResponse("Invalid device data from API"));

                var device = response.Data as GRDConnectDevice;
                return (device, new ErrorResponse());
            }
            catch (Exception ex)
            {
                return (null, new ErrorResponse(ex.Message).WithException(ex));
            }
        }
    }
}