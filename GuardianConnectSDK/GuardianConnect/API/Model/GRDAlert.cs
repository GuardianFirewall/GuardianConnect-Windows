using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model;

/// <summary>
/// A single privacy alert (tracker or page-hijacker the VPN node blocked or
/// detected for this device). Mirrors the shared <c>GRDEvent</c> model on
/// iOS/macOS (Core Data) and Android (SQLite): the same seven fields, fetched
/// from the gateway alerts endpoint via <see cref="GRDGateway.GetAlerts"/> and
/// persisted device-locally on the client.
///
/// Wire-format note: the server sends the unique id under the JSON key
/// <c>uuid</c> and the time as a Unix timestamp (seconds). The category keys
/// match the other platforms exactly (see <see cref="GRDAlertCategory"/>).
/// </summary>
public class GRDAlert
{
    /// Server-issued unique id. Wire key is "uuid"; used as the dedup/primary key.
    [JsonPropertyName("uuid")]
    public string Identifier { get; set; } = string.Empty;

    /// Event time as a Unix timestamp in seconds (may be fractional). Use
    /// <see cref="TimestampUtc"/> for the resolved instant.
    [JsonPropertyName("timestamp")]
    public double Timestamp { get; set; }

    /// "drop" => the connection was BLOCKED; anything else => DETECTED.
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// One of the GRDAlertCategory keys (e.g. "privacy-tracker-mail").
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// Display title, e.g. "Mail Tracker", "Location Tracker".
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// Blocked hostname / domain.
    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    /// Human-readable detail.
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// Resolved event instant (UTC). Tolerates fractional seconds.
    [JsonIgnore]
    public DateTimeOffset TimestampUtc =>
        DateTimeOffset.FromUnixTimeMilliseconds((long)(Timestamp * 1000.0));

    /// True when the node dropped the connection (vs merely detecting it).
    [JsonIgnore]
    public bool WasBlocked =>
        string.Equals(Action, "drop", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The four alert category keys, shared verbatim with iOS/macOS/Android so the
/// category filtering and iconography line up across platforms.
/// </summary>
public static class GRDAlertCategory
{
    public const string MailTracker     = "privacy-tracker-mail";
    public const string LocationTracker = "privacy-tracker-app-location";
    public const string DataTracker     = "privacy-tracker-app";
    public const string PageHijacker    = "ads/aggressive";
}
