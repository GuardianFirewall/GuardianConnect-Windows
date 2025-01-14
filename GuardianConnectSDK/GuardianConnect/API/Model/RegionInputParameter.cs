using Newtonsoft.Json;

namespace GuardianConnect.API.Model
{
    public class RegionInputParameter
    {
        [JsonProperty("region")] public string? Region;
    }
}
