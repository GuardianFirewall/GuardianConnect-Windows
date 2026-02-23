
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using GuardianConnect.Credentials;
using GuardianConnect.Shared;

namespace GuardianConnect.API
{
    public class GRDConnectSubscriber
    {
        // Constants
        public const string kGuardianConnectSubscriber = "kGuardianConnectSubscriber";
        public const string kGuardianConnectSubscriberIdentifierKey = "ep-grd-subscriber-identifier";
        public const string kGuardianConnectSubscriberSecretKey = "ep-grd-subscriber-secret";
        public const string kGuardianConnectSubscriberEmailKey = "ep-grd-subscriber-email";
        public const string kGuardianConnectSubscriberPETNicknameKey = "ep-grd-subscriber-pet-nickname";
        public const string kGuardianConnectSubscriberSubscriptionSKUKey = "ep-grd-subscription-sku";
        public const string kGuardianConnectSubscriberSubscriptionNameFormattedKey = "ep-grd-subscription-name-formatted";
        public const string kGuardianConnectSubscriberSubscriptionExpirationDateKey = "ep-grd-subscription-expiration-date";
        public const string kGuardianConnectSubscriberCreatedAtKey = "ep-grd-subscriber-created-at";

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
        public DateTime SubscriptionExpirationDate { get; set; }

        [JsonPropertyName("ep-grd-subscriber-created-at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("ep-grd-device")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GRDConnectDevice? Device { get; set; }

        // Convenience: Create from dictionary
        public static GRDConnectSubscriber InitFromDictionary(Dictionary<string, object> dict)
        {
	        /*
	         *	DateTime now = DateTime.Now;
				string serialized = now.ToString("DOO"); // e.g., "2024-04-14T16:36:01.5305961+01:00"   

				DateTime deserialized = DateTime.Parse(serialized, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);   
	         */
            var subscriber = new GRDConnectSubscriber
            {
                Identifier = (string)dict[kGuardianConnectSubscriberIdentifierKey],
                Secret = (string)dict[kGuardianConnectSubscriberSecretKey],
                Email = (string)dict[kGuardianConnectSubscriberEmailKey],
                SubscriptionSKU = (string)dict[kGuardianConnectSubscriberSubscriptionSKUKey],
                SubscriptionNameFormatted = (string)dict[kGuardianConnectSubscriberSubscriptionNameFormattedKey],
                SubscriptionExpirationDate = DateTime.Parse((string)dict[kGuardianConnectSubscriberSubscriptionExpirationDateKey],
                                             CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CreatedAt = DateTime.Parse((string)dict[kGuardianConnectSubscriberCreatedAtKey], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            };
            var device = GRDConnectDevice.GetCurrentDeviceAsync();
            return subscriber;
        }

        // Retrieve current subscriber from secure storage
        public static async Task<(GRDConnectSubscriber? Subscriber, ErrorResponse errorResponse)> GetCurrentSubscriberAsync()
        {
            try
            {
	            int retVal = GRDKeychain.ReadDictionaryOfObjects(kGuardianConnectSubscriber, out var binaryDict);

                if (binaryDict == null || binaryDict.Count == 0 || retVal != 0)
                    return (null, new ErrorResponse("Failed to retrieve ConnectSubscriber from registry", IsErrorArg:true));

                var objectDict = new Dictionary<string, object>();
                objectDict.Add(kGuardianConnectSubscriberIdentifierKey, Encoding.UTF8.GetString(binaryDict[kGuardianConnectSubscriberIdentifierKey]));
                objectDict.Add(kGuardianConnectSubscriberSubscriptionSKUKey, Encoding.UTF8.GetString(binaryDict[kGuardianConnectSubscriberSubscriptionSKUKey]));
                objectDict.Add(kGuardianConnectSubscriberEmailKey, Encoding.UTF8.GetString(binaryDict[kGuardianConnectSubscriberEmailKey]));
                objectDict.Add(kGuardianConnectSubscriberSubscriptionNameFormattedKey, Encoding.UTF8.GetString(binaryDict[kGuardianConnectSubscriberSubscriptionNameFormattedKey]));
                objectDict.Add(kGuardianConnectSubscriberCreatedAtKey, DateTime.Parse(Encoding.UTF8.GetString(binaryDict[kGuardianConnectSubscriberCreatedAtKey])));
                objectDict.Add(kGuardianConnectSubscriberSubscriptionExpirationDateKey, Encoding.UTF8.GetString(binaryDict[kGuardianConnectSubscriberSubscriptionExpirationDateKey]));
                
                
                var subscriber = InitFromDictionary(objectDict);
                subscriber.Secret = GRDKeychain.GetPasswordStringForAccount(kGuardianConnectSubscriberSecretKey);

                var (device, deviceError) = await GRDConnectDevice.GetCurrentDeviceAsync(); // TODO: Change to use ErrorResponse
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
        public async Task<ErrorResponse> StoreAsync()
        {
            try
            {
                var secret = Secret;
                Secret = ""; // Do not store secret in user defaults
                
                // Store secret in keychain
                GRDKeychain.StorePassword(secret, kGuardianConnectSubscriberSecretKey);
                
                var dict = new Dictionary<string, byte[]>
                {
                    [kGuardianConnectSubscriberIdentifierKey] = Encoding.UTF8.GetBytes(Identifier), // round trip: string original = Encoding.UTF8.GetString(byteArray);   
                    [kGuardianConnectSubscriberEmailKey] = Encoding.UTF8.GetBytes(Email ?? ""),
                    [kGuardianConnectSubscriberSubscriptionSKUKey] = Encoding.UTF8.GetBytes(SubscriptionSKU),
                    [kGuardianConnectSubscriberSubscriptionNameFormattedKey] = Encoding.UTF8.GetBytes(SubscriptionNameFormatted),
                    [kGuardianConnectSubscriberSubscriptionExpirationDateKey] = Encoding.UTF8.GetBytes(SubscriptionExpirationDate.ToString("O")),
                    [kGuardianConnectSubscriberCreatedAtKey] = Encoding.UTF8.GetBytes(CreatedAt.ToString("O")),
                    [GRDConnectDevice.kGuardianConnectDevice] = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
                };

                GRDKeychain.StoreDictionaryOfObjects(kGuardianConnectSubscriber, dict);

                if (Device != null)
                {
                    var deviceErr = await Device.StoreAsync();
                    if (! deviceErr.IsError)
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
        public static async Task<ErrorResponse> DestroySubscriberAsync()
        {
            try
            {
                GRDKeychain.RemoveKeychainItemForAccount(kGuardianConnectSubscriberSecretKey);
                GRDKeychain.RemoveKeychainItemForAccount("kKeychainStr_PEToken");
                GRDKeychain.RemoveKeychainItemForAccount("kGuardianPETokenExpirationDate");
                GRDKeychain.RemoveSubKeyAndValues(kGuardianConnectSubscriber);
                return new ErrorResponse(null);
            }
            catch (Exception ex)
            {
	            return new ErrorResponse(ex.Message).WithException(ex);
            }
        }

        // List all devices for this subscriber
        public async Task<(List<GRDConnectDevice>? Devices, ErrorResponse Error)> AllDevicesAsync()
        {
            try
            {
                var (currentDevice, deviceError) = await GRDConnectDevice.GetCurrentDeviceAsync();
                if (deviceError.IsError)
                    return (null, deviceError);

                (var devices, var response) = await GRDHousekeepingAPI.RequestAllConnectDevicesForSubscriberAsync(Identifier, Secret);
                if (response.IsError)
                    return (new List<GRDConnectDevice>(), response);

                foreach (var dict in devices)
                {
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

        // Get device reference for current PET [ #168 ]
        public async Task<(GRDConnectDevice? Device, string? Error)> ConnectDeviceReferenceAsync()
        {
            try
            {
                var peToken = GRDPEToken.GetCurrentPEToken();
                if (peToken == null)
                    return (null, "No PE-Token present on device");

                var (deviceDetails, error) = await GRDHousekeepingAPI.ConnectDeviceReferenceForSubscriberAsync(Identifier, Secret, peToken.Token);
                if (error != null)
                    return (null, $"Failed to obtain Connect Device reference: {error}");

                var device = GRDConnectDevice.InitFromDictionary(deviceDetails);
                device.PEToken = peToken.Token;
                device.PETExpires = peToken.ExpirationDate;
                device.IsCurrentDevice = true;
                return (device, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        // Register new subscriber [ #169 ]
        public async Task<(GRDConnectSubscriber? Subscriber, string? Error)> RegisterNewConnectSubscriberAsync(bool acceptedTOS, string deviceNickname)
        {
            if (string.IsNullOrEmpty(Identifier) || string.IsNullOrEmpty(Secret))
                return (null, "Unable to register new Connect subscriber. Either the Connect identifier or secret is missing");

            if (Email == null)
                Email = "";

            var (subscriberDetails, errorMessage) = await GRDHousekeepingAPI.NewConnectSubscriberAsync(Identifier, Secret, deviceNickname, acceptedTOS, Email);
            if (errorMessage != null)
                return (null, errorMessage);

            var newSubscriber = FromDictionary(subscriberDetails);

            var pet = subscriberDetails.TryGetValue("pe-token", out var petObj) ? petObj?.ToString() : null;
            if (string.IsNullOrEmpty(pet))
                return (null, "Failed to register new Connect Subscriber. No PE-Token was returned");

            var petExpires = subscriberDetails.TryGetValue("pet-expires", out var petExpObj) && long.TryParse(petExpObj?.ToString(), out var petExpUnix)
                ? DateTimeOffset.FromUnixTimeSeconds(petExpUnix).DateTime
                : (DateTime?)null;

            // Store PET and expiration date
            GRDKeychain.SetPasswordStringForAccount(pet, "kKeychainStr_PEToken");
            if (petExpires.HasValue)
                GRDKeychain.SetPasswordStringForAccount(petExpires.Value.ToString("o"), "kGuardianPETokenExpirationDate");

            newSubscriber.Secret = Secret;
            var storeErr = await newSubscriber.StoreAsync();
            if (storeErr != null)
                return (null, $"Failed to store persistent local data of new Connect Subscriber: {storeErr}");

            return (newSubscriber, null);
        }

        // Check Guardian account setup state
        public async Task<string?> CheckGuardianAccountSetupStateAsync()
        {
            var error = await GRDHousekeepingAPI.CheckConnectSubscriberGuardianAccountCreationStateAsync(Identifier, Secret);
            return error;
        }

        // Update subscriber email [ #171 ]
        public async Task<(GRDConnectSubscriber? Subscriber, string? Error)> UpdateConnectSubscriberWithEmailAddressAsync(string email)
        {
            if (string.IsNullOrEmpty(email))
                return (null, "New E-Mail is either nil or an empty string. Neither are valid");

            var (subscriberDetails, errorMessage) = await GRDHousekeepingAPI.UpdateConnectSubscriberEmailAsync(email, Identifier, Secret);
            if (errorMessage != null)
                return (null, errorMessage);

            var subscriber = FromDictionary(subscriberDetails);
            var updateErr = await subscriber.StoreAsync();
            if (updateErr != null)
                return (null, $"Failed to store persistent local data of updated Connect Subscriber: {updateErr}");

            return (subscriber, null);
        }

        // Validate subscriber subscription [ #172 ]
        public async Task<(GRDConnectSubscriber? Subscriber, string? Error)> ValidateConnectSubscriberAsync()
        {
            var oldPET = GRDKeychain.GetPasswordStringForAccount("kKeychainStr_PEToken");
            if (string.IsNullOrEmpty(oldPET))
                return (null, "Failed to validate Connect subscriber. No PE-Token present on device");

            var (details, errorMessage) = await GRDHousekeepingAPI.ValidateConnectSubscriberAsync(Identifier, Secret, oldPET);
            if (errorMessage != null)
                return (null, errorMessage);

            var newSubscriber = InitFromDictionary(details);
            newSubscriber.SubscriptionSKU = details.TryGetValue(kGuardianConnectSubscriberSubscriptionSKUKey, out var sku) ? sku?.ToString() ?? "" : "";
            newSubscriber.SubscriptionNameFormatted = details.TryGetValue(kGuardianConnectSubscriberSubscriptionNameFormattedKey, out var name) ? name?.ToString() ?? "" : "";
            newSubscriber.SubscriptionExpirationDate = details.TryGetValue(kGuardianConnectSubscriberSubscriptionExpirationDateKey, out var exp) && long.TryParse(exp?.ToString(), out var expUnix)
                ? DateTimeOffset.FromUnixTimeSeconds(expUnix).DateTime
                : DateTime.MinValue;

            var pet = details.TryGetValue("pe-token", out var petObj) ? petObj?.ToString() : null;
            if (string.IsNullOrEmpty(pet))
                return (null, "Failed to validate Connect Subscriber. No new PE-Token was returned");

            var petExpires = details.TryGetValue("pet-expires", out var petExpObj) && long.TryParse(petExpObj?.ToString(), out var petExpUnix)
                ? DateTimeOffset.FromUnixTimeSeconds(petExpUnix).DateTime
                : (DateTime?)null;

            GRDKeychain.StorePassword(pet, "kKeychainStr_PEToken");
            if (petExpires.HasValue)
                GRDKeychain.StorePassword(petExpires.Value.ToString("O"), "kGuardianPETokenExpirationDate");

            var updateErr = await newSubscriber.StoreAsync();
            if (updateErr != null)
                return (null, $"Failed to store persistent local data of validated Connect Subscriber: {updateErr}");

            return (newSubscriber, null);
        }

        // Logout subscriber [ #173 ]
        public async Task<string?> LogoutConnectSubscriberAsync()
        {
            var pet = GRDKeychain.GetPasswordStringForAccount("kKeychainStr_PEToken");
            if (string.IsNullOrEmpty(pet))
                return "Failed to validate Connect subscriber. No PE-Token present on device";

            var error = await GRDHousekeepingAPI.LogoutConnectSubscriberAsync(pet);
            return error;
        }
    }
}