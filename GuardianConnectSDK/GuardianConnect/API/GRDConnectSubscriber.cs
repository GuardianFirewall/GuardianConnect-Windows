
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Text.Json.Serialization;
using GuardianConnect.Credentials;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;

namespace GuardianConnect.API
{
    public class GRDConnectSubscriber
    {
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
                Identifier = (string)dict[Common.kGuardianConnectSubscriberIdentifierKey],
                Secret = (string)dict[Common.kGuardianConnectSubscriberSecretKey],
                Email = (string)dict[Common.kGuardianConnectSubscriberEmailKey],
                SubscriptionSKU = (string)dict[Common.kGuardianConnectSubscriberSubscriptionSKUKey],
                SubscriptionNameFormatted = (string)dict[Common.kGuardianConnectSubscriberSubscriptionNameFormattedKey],
                SubscriptionExpirationDate = DateTime.Parse((string)dict[Common.kGuardianConnectSubscriberSubscriptionExpirationDateKey],
                                             CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CreatedAt = DateTime.Parse((string)dict[Common.kGuardianConnectSubscriberCreatedAtKey], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            };
            var device = GRDConnectDevice.GetCurrentDeviceAsync();
            return subscriber;
        }

        // Retrieve current subscriber from secure storage
        public static async Task<(GRDConnectSubscriber? Subscriber, ErrorResponse errorResponse)> GetCurrentSubscriberAsync()
        {
            try
            {
	            int retVal = GRDKeychain.ReadDictionaryOfObjects(Common.kGuardianConnectSubscriber, out var binaryDict);

                if (binaryDict == null || binaryDict.Count == 0 || retVal != 0)
                    return (null, new ErrorResponse("Failed to retrieve ConnectSubscriber from registry", IsErrorArg:true));

                var objectDict = new Dictionary<string, object>();
                objectDict.Add(Common.kGuardianConnectSubscriberIdentifierKey, Encoding.UTF8.GetString(binaryDict[Common.kGuardianConnectSubscriberIdentifierKey]));
                objectDict.Add(Common.kGuardianConnectSubscriberSubscriptionSKUKey, Encoding.UTF8.GetString(binaryDict[Common.kGuardianConnectSubscriberSubscriptionSKUKey]));
                objectDict.Add(Common.kGuardianConnectSubscriberEmailKey, Encoding.UTF8.GetString(binaryDict[Common.kGuardianConnectSubscriberEmailKey]));
                objectDict.Add(Common.kGuardianConnectSubscriberSubscriptionNameFormattedKey, Encoding.UTF8.GetString(binaryDict[Common.kGuardianConnectSubscriberSubscriptionNameFormattedKey]));
                objectDict.Add(Common.kGuardianConnectSubscriberCreatedAtKey, DateTime.Parse(Encoding.UTF8.GetString(binaryDict[Common.kGuardianConnectSubscriberCreatedAtKey])));
                objectDict.Add(Common.kGuardianConnectSubscriberSubscriptionExpirationDateKey, Encoding.UTF8.GetString(binaryDict[Common.kGuardianConnectSubscriberSubscriptionExpirationDateKey]));
                
                
                var subscriber = InitFromDictionary(objectDict);
                subscriber.Secret = GRDKeychain.GetPasswordStringForAccount(Common.kGuardianConnectSubscriberSecretKey);

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
                    [Common.kGuardianConnectSubscriberIdentifierKey] = Encoding.UTF8.GetBytes(Identifier), // round trip: string original = Encoding.UTF8.GetString(byteArray);   
                    [Common.kGuardianConnectSubscriberEmailKey] = Encoding.UTF8.GetBytes(Email ?? ""),
                    [Common.kGuardianConnectSubscriberSubscriptionSKUKey] = Encoding.UTF8.GetBytes(SubscriptionSKU),
                    [Common.kGuardianConnectSubscriberSubscriptionNameFormattedKey] = Encoding.UTF8.GetBytes(SubscriptionNameFormatted),
                    [Common.kGuardianConnectSubscriberSubscriptionExpirationDateKey] = Encoding.UTF8.GetBytes(SubscriptionExpirationDate.ToString("O")),
                    [Common.kGuardianConnectSubscriberCreatedAtKey] = Encoding.UTF8.GetBytes(CreatedAt.ToString("O")),
                    [Common.kGuardianConnectDevice] = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
                };

                GRDKeychain.StoreDictionaryOfObjects(Common.kGuardianConnectSubscriber, dict);

                if (Device != null)
                {
                    var deviceErr = Device.Store();
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
                GRDKeychain.RemoveKeychainItemForAccount(Common.kGuardianConnectSubscriberSecretKey);
                GRDKeychain.RemoveKeychainItemForAccount("kKeychainStr_PEToken");
                GRDKeychain.RemoveKeychainItemForAccount("kGuardianPETokenExpirationDate");
                GRDKeychain.RemoveSubKeyAndValues(Common.kGuardianConnectSubscriber);
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
                var (currentDevice, deviceError) = await GRDConnectDevice.GetCurrentDeviceAsync();
                if (deviceError.IsError)
                    return (null, deviceError);

                (var listOfDevicesDict, var response) =
                    await GRDHousekeepingAPI.RequestAllConnectDevicesForSubscriberAsync(GRDVPNHelper.Singleton.PeToken.Token, Identifier , Secret);
                if (response.IsError)
                    return (new List<GRDConnectDevice>(), response);

                foreach (var dict in listOfDevicesDict)
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

        // Get device reference for current PET [ #168  - calls #186] - DONE
        public async Task<(GRDConnectDevice? Device, ErrorResponse errorResponse)> ConnectDeviceReferenceAsync()
        {
            try
            {
                var peToken = GRDPEToken.GetCurrentPEToken();
                if (peToken == null)
                    return (null, new ErrorResponse("No PE-Token present on device"));

                var (deviceDetails, error) =
                    await GRDHousekeepingAPI.GetDeviceReferenceForConnectSubscriberAsync(Identifier, Secret, peToken.Token);
                
                if (error != null)
                    return (null,
                        new ErrorResponse($"Failed to obtain Connect Device reference: {error}"));

                var device = GRDConnectDevice.InitFromDictionary(deviceDetails);
                device.PEToken = peToken.Token;
                device.PETExpires = peToken.ExpirationDate;
                device.IsCurrentDevice = true;
                return (device, new ErrorResponse());
            }
            catch (Exception ex)
            {
                return (null, ErrorResponse.FromException(ex));
            }
        }

        // Register new subscriber [ #169 - calls #185 ]
        public async Task<(GRDConnectSubscriber? Subscriber, ErrorResponse)> RegisterNewConnectSubscriberAsync(bool acceptedTOS, string deviceNickname)
        {
            if (string.IsNullOrEmpty(Identifier) || string.IsNullOrEmpty(Secret))
                return (null,
                    new ErrorResponse("Unable to register new Connect subscriber. Either the Connect identifier or secret is missing"));

            if (Email == null)
                Email = "";

            var (subscriberDetailsDict, errorResponse) =
                await GRDHousekeepingAPI.AddNewConnectSubscriberAsync(Identifier, Secret, deviceNickname, Email,
                    acceptedTOS);
            if (errorResponse.IsError)
                return (null, errorResponse);

            var newSubscriber = InitFromDictionary(subscriberDetailsDict);

            var pet = subscriberDetailsDict.TryGetValue("pe-token", out var petObj) ? petObj?.ToString() : null;
            if (string.IsNullOrEmpty(pet))
                return (null, new ErrorResponse("Failed to register new Connect Subscriber. No PE-Token was returned"));

            var petExpires = subscriberDetailsDict.TryGetValue("pet-expires", out var petExpObj) && long.TryParse(petExpObj?.ToString(), out var petExpUnix)
                ? DateTimeOffset.FromUnixTimeSeconds(petExpUnix).DateTime
                : (DateTime?)null;

            // Store PET and expiration date
            GRDPEToken petFromConnectSubscriber = GRDPEToken.InitFromDictionary(subscriberDetailsDict);
            if (petFromConnectSubscriber != null && petFromConnectSubscriber.Token != null)
                petFromConnectSubscriber.Store();
            
            newSubscriber.Secret = Secret;
            newSubscriber.Store();

            return (newSubscriber, null);
        }

        // Check Guardian account setup state
        // [#170 - calls #190]
        public async Task<ErrorResponse> CheckGuardianAccountSetupStateAsync()
        {
            var errorResponse = await GRDHousekeepingAPI.CheckAccountCreationStateAsync(Identifier, Secret);
            return errorResponse;
        }

        // Update subscriber email [ #171 ]
        public async Task<(GRDConnectSubscriber? Subscriber, ErrorResponse errorResponse)> UpdateConnectSubscriberWithEmailAddressAsync(string email)
        {
            if (string.IsNullOrEmpty(email))
                return (null, new ErrorResponse("New E-Mail is either nil or an empty string. Neither are valid"));

            var (subscriberDetails, errorResponse) =
                await GRDHousekeepingAPI.UpdateConnectSubscriberWithEmailAsync(
                    Identifier, Secret, email, AcceptedTOS, Secret);
            if (errorResponse.IsError)
                return (null, errorResponse);

            var subscriber = InitFromDictionary(subscriberDetails);
            var updateErr = subscriber.Store();
            if (updateErr.IsError)
                return (null, updateErr);

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
            newSubscriber.SubscriptionSKU = details.TryGetValue(Common.kGuardianConnectSubscriberSubscriptionSKUKey, out var sku) ? sku?.ToString() ?? "" : "";
            newSubscriber.SubscriptionNameFormatted = details.TryGetValue(Common.kGuardianConnectSubscriberSubscriptionNameFormattedKey, out var name) ? name?.ToString() ?? "" : "";
            newSubscriber.SubscriptionExpirationDate = details.TryGetValue(Common.kGuardianConnectSubscriberSubscriptionExpirationDateKey, out var exp) && long.TryParse(exp?.ToString(), out var expUnix)
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