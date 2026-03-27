using System.Text.Json.Serialization;

namespace GuardianConnect.Credentials;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GRDPEToken))]
[JsonSerializable(typeof(List<GRDPEToken>))]
public partial class GRDPETokenJsonContext : JsonSerializerContext
{
}
/* Usage
using System.Text.Json;
using GuardianConnect.Credentials;

// Serialize a single object
string json = JsonSerializer.Serialize(token, GRDPETokenJsonContext.Default.GRDPEToken);

// Serialize a list
string jsonList = JsonSerializer.Serialize(tokenList, GRDPETokenJsonContext.Default.ListGRDPEToken);

// Deserialize a single object
var token = JsonSerializer.Deserialize<GRDPEToken>(json, GRDPETokenJsonContext.Default.GRDPEToken);

// Deserialize a list
var tokenList = JsonSerializer.Deserialize<List<GRDPEToken>>(jsonList, GRDPETokenJsonContext.Default.ListGRDPEToken);

// Same for GRDSubscriberCredential and List<GRDSubscriberCredential>

*/