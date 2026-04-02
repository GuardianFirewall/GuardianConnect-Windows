using System.Text.Json.Serialization;

namespace GuardianConnect.Shared;

public enum ConnectionStateEnum
{
    Connected,
    Connecting,
    Disconnected,
    Disconnecting
}

public class CurrentVPNStatus
{
    [JsonConstructor]
    public CurrentVPNStatus()
    {
        EntryName = string.Empty;
    }

    public CurrentVPNStatus(ConnectionStateEnum state, string entryName)
    {
        ConnectionState = state;
        EntryName = entryName;
    }

    [JsonPropertyName("EntryName")]
    [JsonInclude]
    public string EntryName { get; set; }

    [JsonPropertyName("ConnectionState")]
    [JsonInclude]
    public ConnectionStateEnum ConnectionState { get; set; }
}