//using NativeRoutines;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GuardianConnect.Credentials;

public class GRDSubscriberCredential
{
    [JsonIgnore] private static ILogger _logger = NullLogger.Instance;

    public GRDSubscriberCredential()
    {
    }

    public GRDSubscriberCredential(string subscriberCredential)
    {
        if (string.IsNullOrEmpty(subscriberCredential)) return;

        Jwt = subscriberCredential;
        ParseSubscriberCredentialString();
    }

    [JsonIgnore]
    public static ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance) _logger = StaticLoggerFactory.CreateLogger("GRDSubscriberCredential");
            return _logger;
        }
    }

    public static GRDSubscriberCredential GetCurrentStoredSubscriberCredential()
    {
        // CONN#8
        Logger.LogDebug("CONN#8");

        var subCredBytes = GRDKeychain.GetDataForAccount(Common.kKeychainStr_SubscriberCredential);
        var subscriberCredential = new GRDSubscriberCredential("");
        if (subCredBytes.Length > 0)
            subscriberCredential =
                JsonSerializer.Deserialize<GRDSubscriberCredential>(subCredBytes,
                    GRDSubscriberCredentialJsonContext.Default.GRDSubscriberCredential) ??
                throw new InvalidOperationException();

        var sc = new GRDSubscriberCredential(subscriberCredential.Jwt);

        return sc;
    }

    public void Store()
    {
        Logger.LogInformation("Storing SubscriberCredentials to keychain...");
        var jsonOut =
            JsonSerializer.Serialize(this, GRDSubscriberCredentialJsonContext.Default.GRDSubscriberCredential);
        var bytes = Encoding.UTF8.GetBytes(jsonOut);
        GRDKeychain.StoreData(Common.kKeychainStr_SubscriberCredential, bytes);
    }

    public GRDSubscriberCredential InitWithSubscriberCredential(string subscriberCredential)
    {
        return new GRDSubscriberCredential(subscriberCredential);
    }

    private static GRDSubscriberCredential CurrentSubscriberCredential()
    {
        var subCredString = GRDKeychain.GetPasswordStringForAccount(IGRDKeychain.kKeychainStr_SubscriberCredential);
        return new GRDSubscriberCredential(subCredString ?? string.Empty);
    }

    public string Description()
    {
        return ToString();
    }

    public override string ToString()
    {
        var expiredString = @"YES";
        if (!IsTokenExpired) expiredString = @"NO";

        return
            $"{GetType()}\nSubscription Type:{SubscriptionType} \nSubscription Expiration Date: {SubscriptionExpirationDate} \nExpired: {expiredString}";
    }

    private void ParseSubscriberCredentialString()
    {
        var jwtSplit = Jwt.Split('.');
        var payloadString = jwtSplit[1];

        // Note from CJ:
        // This is Base64 magic that I only partly understand because I am not entirely familiar with
        // the Base64 spec.
        // This just makes sure that the string can be read by removing invalid characters
        payloadString = payloadString.Replace('_', '/').Replace('-', '+');

        var padSizeForB64 = 4 - payloadString.Length % 4;

        var base64Payload = payloadString + (padSizeForB64 != 4 ? new string('=', padSizeForB64) : "");

        var payLoad = Common.DecodeFrom64(base64Payload);
        Logger.LogInformation($"ParseSubscriberCredentials: jwt payload = '{payLoad}'");
        var subCred = JsonSerializer.Deserialize<GRDSubscriberCredential>(payLoad,
                          GRDSubscriberCredentialJsonContext.Default.GRDSubscriberCredential)
                      ?? throw new InvalidOperationException("Failed to deserialize subscriber credential payload");

        SubscriptionType = subCred.SubscriptionType ?? string.Empty;
        SubscriptionTypePretty = subCred.SubscriptionTypePretty ?? string.Empty;

        //long expDateTimeSecondsSinceUnixEpoch = long.Parse(SubscriptionExpirationDateUnixSeconds);
        //SubscriptionExpirationDate = Common.DateOnlyFromAppleDTI1970(expDateTimeSecondsSinceUnixEpoch);
        //SubscriptionExpirationDate = DateTimeOffset.FromUnixTimeSeconds((long)gscDict!["subscription-expiration-date"]).DateTime;
        SubscriptionExpirationDate =
            DateTimeOffset.FromUnixTimeSeconds(subCred.SubscriptionExpirationDateUnixSeconds).DateTime;
        TokenExpirationDate = DateTimeOffset.FromUnixTimeSeconds(subCred.TokenExpirationDateUnixSeconds).DateTime;
        //SubscriptionTypePretty = (string)gscDict["subscription-type"];
        IsTokenExpired = IsExpired();
        //Jwt = subCred.Jwt;
        SubscriptionExpirationDateUnixSeconds = subCred.SubscriptionExpirationDateUnixSeconds;
        TokenExpirationDateUnixSeconds = subCred.TokenExpirationDateUnixSeconds;
    }

    private bool IsExpired()
    {
        var safeExpirationDate = TokenExpirationDate.AddDays(-(Common.FortyEightHoursInSeconds / (3600 * 24)));
        var expired = safeExpirationDate < DateTime.Now;
        return expired;
    }

    #region json properties

    [JsonPropertyName("jwt")] public string Jwt { get; set; } = string.Empty;

    //[JsonPropertyName("sub-type")]
    [JsonPropertyName("subscription-type")]
    public string SubscriptionType { get; set; } = string.Empty;

    [JsonPropertyName("subscription-type-pretty")]
    //[JsonPropertyName("sub-type-pretty")]
    public string SubscriptionTypePretty { get; set; } = string.Empty;

    //[JsonPropertyName("sub-expire-date")]
    [JsonPropertyName("subscription-expiration-date")]
    public long SubscriptionExpirationDateUnixSeconds { get; set; }

    [JsonIgnore] public DateTime SubscriptionExpirationDate { get; set; }

    //[JsonPropertyName("token-expire-date")]
    [JsonPropertyName("exp")] public long TokenExpirationDateUnixSeconds { get; set; }

    [JsonIgnore] public DateTime TokenExpirationDate { get; set; }

    [JsonIgnore] public bool IsTokenExpired { get; set; }

    [JsonIgnore] public bool IsEmpty => string.IsNullOrEmpty(Jwt);

    #endregion json properties
}