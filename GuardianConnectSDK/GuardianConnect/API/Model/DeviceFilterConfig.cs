using System.Text.Json.Serialization;
using GuardianConnect.Credentials;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;

namespace GuardianConnect.API.Model;

public class DeviceFilterConfig
{
    #region Definitions

    [Flags]
    public enum DeviceFilterConfigFlags
    {
        BlocklistCleared = 0,
        BlocklistDisableFirewall = 1 << 0,
        BlocklistBlockAds = 1 << 1,
        BlocklistBlockPhishing = 1 << 2,
        BlocklistMax = 1 << 3
    }

    #endregion

    [JsonIgnore] public DeviceFilterConfigFlags DeviceFilterConfigBlockList;
    private ILogger<DeviceFilterConfig> _logger;

    #region fields

    [JsonPropertyName("api-auth-token")] public string Api_auth_token { get; set; } = string.Empty;

    [JsonPropertyName("block-phishing")]
    public bool Block_Phishing
    {
        get => (DeviceFilterConfigBlockList & DeviceFilterConfigFlags.BlocklistBlockPhishing) != 0;
        set => throw new NotImplementedException();
    }

    [JsonPropertyName("block-ads")]
    //[Newtonsoft.Json.JsonConverter(typeof(YesNoConverter))]
    public bool Block_Ads
    {
        get => (DeviceFilterConfigBlockList & DeviceFilterConfigFlags.BlocklistBlockAds) != 0;
        set => throw new NotImplementedException();
    }

    [JsonPropertyName("block-none")]
    public bool Disable_Firewall
    {
        get => (DeviceFilterConfigBlockList & DeviceFilterConfigFlags.BlocklistDisableFirewall) != 0;
        set => throw new NotImplementedException();
    }

    #endregion

    #region methods

    [JsonConstructor]
    public DeviceFilterConfig()
    {
        _logger = StaticLoggerFactory.CreateLogger<DeviceFilterConfig>();
        DeviceFilterConfigBlockList = DeviceFilterConfigFlags.BlocklistCleared;
        var mc = GRDCredentialManager.GetMainCredentials();
        Api_auth_token = mc == null ? "" :
            mc.ApiAuthToken == null ? "" :
            mc.ApiAuthToken;
    }

    public void Toggle(DeviceFilterConfigFlags flag)
    {
        DeviceFilterConfigBlockList ^= flag;
    }

    public void Set(DeviceFilterConfigFlags flag)
    {
        DeviceFilterConfigBlockList |= flag;
        SyncBlocklist();
    }

    public void Clear(DeviceFilterConfigFlags flag)
    {
        DeviceFilterConfigBlockList &= ~flag;
    }

    public void Reset()
    {
        DeviceFilterConfigBlockList = 0;
    }

    public void SyncBlocklist()
    {
        if (GRDVPNHelper.Singleton.CurrentDeviceBlocklistConfig != null)
            Preferences.Set(Common.kGRDDeviceFilterConfigBlocklist,
                GRDVPNHelper.Singleton.CurrentDeviceBlocklistConfig.ToString());
        // Call GRDGateway setter.
        GRDGateway.SetDeviceFilterConfigsForDeviceId();
    }

    public override string ToString()
    {
        var json = new List<string>();
        return string.Join(Environment.NewLine, json);
    }

    #endregion
}