namespace GuardianConnect.Abstractions;

/// <summary>
/// Kill switch mode. v1 ships only Off and OnConnected. Always-On (persistent
/// filters that survive process exit and reboot) is in §8 Future Experimental of
/// the design doc and not implemented in v1.
/// </summary>
public enum KillSwitchMode
{
    /// <summary>No filters installed. Default.</summary>
    Off = 0,

    /// <summary>
    /// Filters active while VPN is connecting/connected/reconnecting. Removed on
    /// user-initiated disconnect; kept across unexpected drops so traffic stays
    /// blocked until the user either re-establishes the tunnel or disables the
    /// kill switch.
    /// </summary>
    OnConnected = 1,
}

/// <summary>
/// Snapshot of kill switch state, returned over IPC to the client UI.
/// </summary>
public sealed class KillSwitchStatus
{
    public KillSwitchMode Mode { get; init; }
    public bool AllowLan { get; init; }

    /// <summary>
    /// True when filters are currently installed (block-all is in force). Read-only;
    /// driven by the service based on Mode and the live VPN state.
    /// </summary>
    public bool IsActive { get; init; }
}
