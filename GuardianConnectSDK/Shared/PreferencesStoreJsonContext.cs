using System.Text.Json.Serialization;
using static GuardianConnect.Shared.Preferences;

namespace GuardianConnect.Shared
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(PreferencesStore))]
    [JsonSerializable(typeof(List<PreferencesStore>))]
    [JsonSerializable(typeof(List<string>))]
    public partial class PreferencesStoreJsonContext : JsonSerializerContext
    {
    }
}
