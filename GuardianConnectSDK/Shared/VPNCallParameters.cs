namespace GuardianConnect.Shared
{
    public class VPNCallParameters
    {
        public string EapuserName { get; set; } = string.Empty;
        public string Eappassword { get; set; } = string.Empty;
        public string VpnHostName { get; set; } = string.Empty;
        public string VpnHostDisplay { get; set; } = string.Empty;
        public string EntryName { get; set; } = string.Empty;

        public VPNCallParameters()
        {
        }
    }
}
