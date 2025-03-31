using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using Newtonsoft.Json;

namespace GuardianConnect.API.Model;

public class DeviceFilterConfig
{
    [Newtonsoft.Json.JsonIgnore]
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
    [JsonProperty("api-auth-token")]
    public string Api_auth_token = string.Empty;

    [JsonProperty("block-phishing")]
    //[Newtonsoft.Json.JsonConverter(typeof(YesNoConverter))]
    public bool Block_Phishing => (DeviceFilterConfigBlockList & DeviceFilterConfigFlags.BlocklistBlockPhishing) != 0;

    [JsonProperty("block-ads")]
    //[Newtonsoft.Json.JsonConverter(typeof(YesNoConverter))]
    public bool Block_Ads => (DeviceFilterConfigBlockList & DeviceFilterConfigFlags.BlocklistBlockAds) != 0;

    [JsonProperty("block-none")]
    //[Newtonsoft.Json.JsonConverter(typeof(YesNoConverter))]
    public bool Disable_Firewall => (DeviceFilterConfigBlockList & DeviceFilterConfigFlags.BlocklistDisableFirewall) != 0;
    
    #endregion

    #region methods
    public DeviceFilterConfig()
    {
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
        if (GRDVPNHelper.Instance.CurrentDeviceBlocklistConfig != null)
            Preferences.Set(Common.kGRDDeviceFilterConfigBlocklist,
                GRDVPNHelper.Instance.CurrentDeviceBlocklistConfig.ToString());
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