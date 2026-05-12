namespace GuardianConnect.Shared;

public class VPNCallParameters
{
    public string EapuserName { get; set; } = string.Empty;
    public string Eappassword { get; set; } = string.Empty;
    public string VpnHostName { get; set; } = string.Empty;
    public string VpnHostDisplay { get; set; } = string.Empty;
    public string EntryName { get; set; } = string.Empty;

    /// <summary>
    /// Filesystem path to a wg-quick .conf file. Used by VpnTunnelManager when
    /// the requested transport is WireGuard. Backend-served config delivery
    /// will eventually replace this; for now it lets us drive the transport
    /// from a local file during development.
    /// </summary>
    public string? WireGuardConfigPath { get; set; }

    /// <summary>
    /// Inline wg-quick text. Takes precedence over WireGuardConfigPath when both
    /// are set. Intended for the eventual backend-delivered config flow.
    /// </summary>
    public string? WireGuardConfigText { get; set; }
}
