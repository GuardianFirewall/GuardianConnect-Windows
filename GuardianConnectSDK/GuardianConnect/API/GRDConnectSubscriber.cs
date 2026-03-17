using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GuardianConnect.Credentials;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using Microsoft.Win32.SafeHandles;

namespace GuardianConnect.API
{
    public class GRDConnectSubscriber
    {
        // REMOVE THIS!! TESTING ONLY  - TODO TODO TODO CHECK
        private static Dictionary<string, object> _deviceDict;

        //
        
        // Properties
        [JsonPropertyName("ep-grd-subscriber-identifier")]
        public string Identifier { get; set; }

        [JsonIgnore] // Secret is never serialized; stored separately in the keychain
        public string Secret { get; set; }

        [JsonPropertyName("ep-grd-subscriber-email")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Email { get; set; }

        [JsonPropertyName("ep-grd-subscription-sku")]
        public string SubscriptionSKU { get; set; }

        [JsonPropertyName("ep-grd-subscription-name-formatted")]
        public string SubscriptionNameFormatted { get; set; }

        [JsonPropertyName("ep-grd-subscription-expiration-date")]
        public long SubscriptionExpirationDate { get; set; }

        [JsonPropertyName("ep-grd-subscriber-created-at")]
        public long CreatedAt { get; set; }

        [JsonPropertyName("ep-grd-device")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GRDConnectDevice? Device { get; set; }

        // CHECK WITH CJ ON THIS
        public bool AcceptedTOS { get; set; }
        //

        // Convenience: Create from dictionary
        public static GRDConnectSubscriber InitFromDictionary(Dictionary<string, object> subscriberDetailsDict)
        {
            var subscriber = new GRDConnectSubscriber();
            subscriber.Identifier = (string)subscriberDetailsDict[Common.kGuardianConnectSubscriberIdentifierKey].ToString();
            subscriber.Email = (string)subscriberDetailsDict[Common.kGuardianConnectSubscriberEmailKey].ToString();
            subscriber.SubscriptionSKU = (string)subscriberDetailsDict[Common.kGuardianConnectSubscriberSubscriptionSKUKey].ToString();
            subscriber.SubscriptionNameFormatted = (string)subscriberDetailsDict[Common.kGuardianConnectSubscriberSubscriptionNameFormattedKey].ToString();
            subscriber.SubscriptionExpirationDate = long.Parse((string)subscriberDetailsDict[Common.kGuardianConnectSubscriberSubscriptionExpirationDateKey].ToString());
            subscriber.CreatedAt = long.Parse((string)subscriberDetailsDict[Common.kGuardianConnectSubscriberCreatedAtKey].ToString());

#if OLDWAY
            if (dict.ContainsKey(Common.kGuardianConnectDeviceDictKey))
            {
                var deviceDictSection = dict[Common.kGuardianConnectDeviceDictKey];
                var deviceDict = dict[Common.kGuardianConnectDeviceDictKey] as Dictionary<string, object>;
                //Dictionary<string, object> devDict = JsonSerializer.Deserialize<Dictionary<string, object>>( deviceDictSection);
                deviceDict[Common.kGuardianConnectDevicePETokenKey] = dict[Common.kGuardianConnectDevicePETokenKey].ToString();
                deviceDict[Common.kGuardianPETokenExpirationDate] = long.Parse((string)dict[Common.kGuardianConnectDevicePETExpiresKey].ToString());
                var device = GRDConnectDevice.InitFromDictionary( dict[Common.kGuardianConnectDeviceDictKey] as Dictionary<string, object>);
                subscriber.Device = device;
            }
#else
            if (subscriberDetailsDict.ContainsKey(Common.kGuardianConnectDeviceDictKey))
            {
                var deviceJsonObj = subscriberDetailsDict[Common.kGuardianConnectDeviceDictKey] as JsonObject;
                if (deviceJsonObj != null)
                {
                    var deviceDict = deviceJsonObj.ToDictionary(
                        kvp => kvp.Key,
                        kvp => (object)kvp.Value);

                    // pe-token and pet-expires live at the top level, not inside ep-grd-device
                    deviceDict[Common.kGuardianConnectDevicePETokenKey] =
                        subscriberDetailsDict[Common.kGuardianConnectDevicePETokenKey];
                    deviceDict[Common.kGuardianConnectDevicePETExpiresKey] =
                        subscriberDetailsDict[Common.kGuardianConnectDevicePETExpiresKey];

                    var device = GRDConnectDevice.InitFromDictionary(deviceDict);
                    subscriber.Device = device;
                }
            }
#endif
            return subscriber;
        }

        // Retrieve current subscriber from secure storage
        public static (GRDConnectSubscriber? Subscriber, ErrorResponse errorResponse) GetCurrentSubscriber()
        {
            try
            {
                int retVal = GRDKeychain.ReadDictionaryOfObjects(Common.kGuardianConnectSubscriberStoreKey, out var binaryCSDict);
                if (binaryCSDict.Count == 0 || retVal != 0)
                    return (null,
                        new ErrorResponse("Failed to retrieve ConnectSubscriber from registry", IsErrorArg: true));
//                retVal = GRDKeychain.ReadDictionaryOfObjects(Common.kGuardianConnectDeviceStoreKey, out var binaryCDDict);
//                if (binaryCDDict.Count == 0 || retVal != 0)
//                    return (null,
//                        new ErrorResponse("Failed to retrieve ConnectDevice from registry", IsErrorArg: true));

                var objectDict = new Dictionary<string, object>();
                objectDict.Add(Common.kGuardianConnectSubscriberIdentifierKey,
                    Encoding.UTF8.GetString(binaryCSDict[Common.kGuardianConnectSubscriberIdentifierKey]));
                objectDict.Add(Common.kGuardianConnectSubscriberSubscriptionSKUKey,
                    Encoding.UTF8.GetString(binaryCSDict[Common.kGuardianConnectSubscriberSubscriptionSKUKey]));
                objectDict.Add(Common.kGuardianConnectSubscriberEmailKey,
                    Encoding.UTF8.GetString(binaryCSDict[Common.kGuardianConnectSubscriberEmailKey]));
                objectDict.Add(Common.kGuardianConnectSubscriberSubscriptionNameFormattedKey,
                    Encoding.UTF8.GetString(binaryCSDict[Common.kGuardianConnectSubscriberSubscriptionNameFormattedKey]));
                objectDict.Add(Common.kGuardianConnectSubscriberCreatedAtKey,
                    BitConverter.ToInt64(binaryCSDict[Common.kGuardianConnectSubscriberCreatedAtKey], 0));
                objectDict.Add(Common.kGuardianConnectSubscriberSubscriptionExpirationDateKey,
                        BitConverter.ToInt64(binaryCSDict[Common.kGuardianConnectSubscriberSubscriptionExpirationDateKey], 0));

                var subscriber = InitFromDictionary(objectDict);
                subscriber.Secret = GRDKeychain.GetPasswordStringForAccount(Common.kGuardianConnectSubscriberSecretKey);

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
                var secret = Secret;
                Secret = ""; // Do not store secret in user defaults

                // Store secret in keychain
                GRDKeychain.StorePassword(secret, Common.kGuardianConnectSubscriberSecretKey);

                var dict = new Dictionary<string, byte[]>
                {
                    [Common.kGuardianConnectSubscriberIdentifierKey] =
                        Encoding.UTF8
                            .GetBytes(
                                Identifier), // round trip: string original = Encoding.UTF8.GetString(byteArray);   
                    [Common.kGuardianConnectSubscriberEmailKey] = Encoding.UTF8.GetBytes(Email ?? ""),
                    [Common.kGuardianConnectSubscriberSubscriptionSKUKey] = Encoding.UTF8.GetBytes(SubscriptionSKU),
                    [Common.kGuardianConnectSubscriberSubscriptionNameFormattedKey] =
                        Encoding.UTF8.GetBytes(SubscriptionNameFormatted),
                    [Common.kGuardianConnectSubscriberSubscriptionExpirationDateKey] = BitConverter.GetBytes(SubscriptionExpirationDate),
                    [Common.kGuardianConnectSubscriberCreatedAtKey] = BitConverter.GetBytes(CreatedAt),
                    [Common.kGuardianConnectDeviceStoreKey] = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
                };

                GRDKeychain.StoreDictionaryOfObjects(Common.kGuardianConnectSubscriberStoreKey, dict);

                if (Device != null)
                {
                    var deviceErr = Device.Store();
                    if (!deviceErr.IsError)
                        return deviceErr;
                }

                return new ErrorResponse(null);
            }
            catch (Exception ex)
            {
                return new ErrorResponse(ex.Message).WithException(ex);
            }
        }

        // Destroy subscriber and related secrets
        public static async Task<ErrorResponse> DestroySubscriber()
        {
            try
            {
                GRDKeychain.RemoveKeychainItemForAccount(Common.kGuardianConnectSubscriberSecretKey);
                GRDKeychain.RemoveKeychainItemForAccount(Common.kKeychainStr_PEToken);
                GRDKeychain.RemoveKeychainItemForAccount(Common.kGuardianPETokenExpirationDate);
                GRDKeychain.RemoveSubKeyAndValues(Common.kGuardianConnectSubscriberStoreKey);
                return new ErrorResponse(null);
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
            var devices = new List<GRDConnectDevice>();
            try
            {
                var (currentDevice, deviceError) = GRDConnectDevice.GetCurrentDevice();
                if (deviceError.IsError)
                    return (null, deviceError);

                (var listOfDevices, var response) =
                    await GRDHousekeepingAPI.RequestAllConnectDevicesForSubscriberAsync( null, Identifier, Secret);
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
                foreach (var item in listOfDevices.OfType<JsonObject>())
                {
                    var dict   = item.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value);
                    var device = GRDConnectDevice.InitFromDictionary(dict);
                    if (currentDevice != null && device.UUID == currentDevice.UUID)
                        device.IsCurrentDevice = true;
                    devices.Add(device);
                }

                return (devices, new ErrorResponse(null));
            }
            catch (Exception ex)
            {
                return (null, new ErrorResponse(ex.Message).WithException(ex));
            }
        }

        // Get device reference for current PET [ #168 - calls #186] - TESTED
        public async Task<(GRDConnectDevice? Device, ErrorResponse errorResponse)> ConnectDeviceReferenceAsync()
        {
            try
            {
                var peToken = GRDPEToken.GetCurrentPEToken();
                if (string.IsNullOrEmpty(peToken.Token))
                    return (null, new ErrorResponse("No PE-Token present on device"));

                var (deviceDetails, error) =
                    await GRDHousekeepingAPI.GetDeviceReferenceForConnectSubscriberAsync(Identifier, Secret,
                        peToken.Token);

                if (error.IsError)
                    return (null,
                        new ErrorResponse($"Failed to obtain Connect Device reference: {error}"));

                // Let's add the stuff we already have
                deviceDetails.Add(Common.kGuardianConnectDevicePETokenKey, peToken.Token);
                deviceDetails.Add(Common.kGuardianPETokenExpirationDate, peToken.ExpirationDateUnix);
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
        public async Task<(GRDConnectSubscriber? Subscriber, ErrorResponse)> RegisterNewConnectSubscriberAsync(
            bool acceptedTOS, string deviceNickname)
        {
            if (string.IsNullOrEmpty(Identifier) || string.IsNullOrEmpty(Secret))
                return (null,
                    new ErrorResponse(
                        "Unable to register new Connect subscriber. Either the Connect identifier or secret is missing"));

            if (Email == null)
                Email = "";

            var (subscriberDetailsDict, errorResponse) =
                await GRDHousekeepingAPI.AddNewConnectSubscriberAsync(Identifier, Secret, deviceNickname, Email, acceptedTOS);
            if (errorResponse.IsError) return (null, errorResponse);

            // First check if a PE-Token was returned
            var petExists = subscriberDetailsDict.TryGetValue(Common.kGuardianConnectDevicePETokenKey, out _);
            if (! petExists)
                return (null, new ErrorResponse("Failed to register new Connect Subscriber. No PE-Token was returned"));

            // Fill in the things we were provided to the API
            if (!subscriberDetailsDict.ContainsKey(Common.kGuardianConnectSubscriberEmailKey))
                subscriberDetailsDict.Add(Common.kGuardianConnectSubscriberEmailKey, Email);
            
            var newSubscriber = InitFromDictionary(subscriberDetailsDict);

            var petExpires = long.Parse(subscriberDetailsDict[Common.kGuardianConnectDevicePETExpiresKey].ToString());

            // Store PET and expiration date
            GRDPEToken petFromConnectSubscriber = GRDPEToken.InitFromDictionary(subscriberDetailsDict);
            petFromConnectSubscriber.Store();
            
            newSubscriber.Secret = Secret;
            newSubscriber.Device = newSubscriber.Device;
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
        public async Task<(GRDConnectSubscriber? Subscriber, ErrorResponse errorResponse)> UpdateConnectSubscriberWithEmailAddressAsync(string email)
        {
            if (string.IsNullOrEmpty(email))
                return (null, new ErrorResponse("New E-Mail is either nil or an empty string. Neither are valid"));
            var currentSubscriber = GetCurrentSubscriber();

            var (subscriberDetails, errorResponse) =
                await GRDHousekeepingAPI.UpdateConnectSubscriberWithEmailAsync( Identifier, Secret, email, AcceptedTOS, Secret);
            if (errorResponse.IsError)
                return (null, errorResponse);

            // FIX THIS: The given key 'ep-grd-subscriber-identifier' was not present in the dictionary.
            var subscriber = InitFromDictionary(subscriberDetails);
            var updateErr = subscriber.Store();
            if (updateErr.IsError)
                return (null, updateErr);

            return (subscriber, null);
        }

        // Validate subscriber subscription [ #172 ] - NOT WORKING
        public async Task<(GRDConnectSubscriber? Subscriber, ErrorResponse errorResponse)> ValidateConnectSubscriberAsync()
        {
            var currentPET = GRDPEToken.GetCurrentPEToken().Token;
            if (string.IsNullOrEmpty(currentPET))
                return (null, new ErrorResponse("Failed to validate Connect subscriber. No PE-Token present on device"));

            var (details, errorResponse) =
                await GRDHousekeepingAPI.ValidateConnectSubscriberAsync(Identifier, Secret, currentPET);
            
            if (errorResponse.IsError)
                return (null, errorResponse);

            var pet = details.TryGetValue("pe-token", out var petObj) ? petObj?.ToString() : null;
            if (string.IsNullOrEmpty(pet))
                return (null, new ErrorResponse("Failed to validate Connect Subscriber. No new PE-Token was returned"));

            details[Common.kGuardianConnectSubscriberIdentifierKey] = JsonSerializer.SerializeToElement(Identifier);
            details[Common.kGuardianConnectSubscriberSecret]        = JsonSerializer.SerializeToElement(Secret);
            details[Common.kGuardianConnectSubscriberEmailKey]      = JsonSerializer.SerializeToElement(Email);
            var newSubscriber = InitFromDictionary(details);

            var petExpires = details[Common.kGuardianPETokenExpirationDate];
            // TODO: Change to actual GRDPEToken and use its Store();
            GRDPEToken petFromConnectSubscriber = new GRDPEToken()
            {
                ExpirationDateUnix = long.Parse(petExpires.ToString())
            };
            petFromConnectSubscriber.Token = pet;
            petFromConnectSubscriber.Store();

            var updateErr = newSubscriber.Store();
            if (updateErr.IsError)
                return (null, updateErr.SetErrorMessage($"Failed to store persistent local data of validated Connect Subscriber: {updateErr.Message}"));

            return (newSubscriber, null);
        }

        // Logout subscriber [ #173 ]
        public async Task<ErrorResponse> LogoutConnectSubscriberAsync()
        {
            var pet = GRDKeychain.GetPasswordStringForAccount("kKeychainStr_PEToken");
            if (string.IsNullOrEmpty(pet))
                return new ErrorResponse("Failed to validate Connect subscriber. No PE-Token present on device");

            var errorResponse = await GRDHousekeepingAPI.LogOutConnectSubscriberAsync(pet);
            return errorResponse;
        }
    }
}