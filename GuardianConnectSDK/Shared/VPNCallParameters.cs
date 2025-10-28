namespace GuardianConnect.Shared
{
    public class VPNCallParameters
    {
        public string EapuserName { get; set; }
        public string Eappassword { get; set; }
        public string VpnHostName { get; set; }
        public string VpnHostDisplay { get; set; }
        public string EntryName { get; set; }

        public VPNCallParameters()
        {
        }
    }
}
