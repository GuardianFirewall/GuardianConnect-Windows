using GuardianConnect.Helpers;
using GuardianConnect.Shared;
//using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model;

public class DeviceFilterConfig
{
    private Microsoft.Extensions.Logging.ILogger<DeviceFilterConfig> _logger;

    [JsonIgnore]
    public DeviceFilterConfigFlags DeviceFilterConfigBlockList;

    #region Definitions
    [Flags]
    public enum DeviceFilterConfigFlags
    {
        BlocklistCleared            = 0,
        BlocklistDisableFirewall 	= (1 << 0),
        BlocklistBlockAds 		    = (1 << 1),
        BlocklistBlockPhishing 	    = (1 << 2),
        BlocklistMax 				= (1 << 3)
    }
    #endregion

    #region fields
    [JsonPropertyName("api-auth-token")]
    public string Api_auth_token { get; set; } = string.Empty;

    [JsonPropertyName("block-phishing")]
    //[Newtonsoft.Json.JsonConverter(typeof(YesNoConverter))]
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
    //[Newtonsoft.Json.JsonConverter(typeof(YesNoConverter))]
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
        GRDGateway gw = new GRDGateway();
        gw.SetDeviceFilterConfigsForDeviceId();
    }

    public override string ToString()
    {
        List<string> json = new List<string>();
        return string.Join(Environment.NewLine, json);
    }

    #endregion
}