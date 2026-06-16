namespace GuardianConnect.Shared;

/// <summary>
/// Single source of truth for the user-selected VPN transport protocol.
/// Replaces both the previous inline enum on <c>ITransportProvider</c>
/// and the raw <c>RegistrySettings.RetrieveGuardianUserSettings(Common.kGuardianTransportProtocol)</c>
/// + magic-string comparison pattern that was duplicated across every
/// dispatch site (GRDVPNHelper, GeneralPageViewModel, AdvancedContentViewModel,
/// DeveloperContentPage, ...).
///
/// The nested <see cref="TransportProtocol"/> enum is identical in shape to
/// the prior <c>ITransportProvider.TransportProtocol</c> — same member
/// names, same ordinal values — so on-disk JSON serialization of
/// <c>GRDCredential.TransportProtocol</c> stays wire-compatible. Credentials
/// persisted by older builds deserialize unchanged after this refactor.
///
/// Lives in <c>GuardianConnect.Shared</c> — the lowest-level project, which
/// hosts <c>RegistrySettings</c> and <c>Common</c> (the only dependencies of
/// the Get/Set methods). Placing it here lets types in Shared (such as
/// <c>VPNCallParameters</c>) name the transport directly, while higher layers
/// like <c>GuardianConnect.Abstractions</c> (e.g. <c>ITransportProvider</c>)
/// still reference it without an upward dependency, since they already
/// depend on Shared.
/// </summary>
public static class GRDTransportProtocol
{
    public enum TransportProtocol
    {
        TransportUnknown,
        TransportIKEv2,
        TransportWireGuard,
    }

    // Registry string values written by the UI radio toggle in
    // AdvancedContentPage and read here. The on-disk format
    // ("IKEv2", "WireGuard") is preserved verbatim from prior versions
    // so this refactor doesn't require a migration step on upgrade.
    private const string IKEv2String     = "IKEv2";
    private const string WireGuardString = "WireGuard";

    /// <summary>
    /// Reads the user's preferred transport from
    /// HKCU\Software\GuardianFirewall\Settings\kGuardianTransportProtocol.
    /// Falls back to <see cref="TransportProtocol.TransportIKEv2"/> when
    /// the value is missing or unrecognised — matching the prior implicit
    /// default at every dispatch site (every previous reader fell through
    /// to the IKEv2 branch whenever the registry value wasn't literally
    /// the string "WireGuard").
    /// </summary>
    public static TransportProtocol GetPreferred()
    {
        var raw = RegistrySettings.RetrieveGuardianUserSettings(Common.kGuardianTransportProtocol);
        if (string.Equals(raw, WireGuardString, StringComparison.OrdinalIgnoreCase))
            return TransportProtocol.TransportWireGuard;
        // Missing / "IKEv2" / unknown → IKEv2.
        return TransportProtocol.TransportIKEv2;
    }

    /// <summary>
    /// Persists the user's preferred transport. Called from the
    /// AdvancedContentPage transport-radio handler and from the Dev
    /// tab's forced-demotion paths (when a WG file-based override has
    /// no valid wg-quick file). Writes the canonical string form
    /// expected by <see cref="GetPreferred"/>.
    /// </summary>
    public static void SetPreferred(TransportProtocol p)
    {
        var s = p switch
        {
            TransportProtocol.TransportWireGuard => WireGuardString,
            TransportProtocol.TransportIKEv2     => IKEv2String,
            _                                    => IKEv2String,
        };
        RegistrySettings.UpdateGuardianUserSettings(Common.kGuardianTransportProtocol, s);
    }

    /// <summary>
    /// Wire-format string used in the <c>transport-protocol</c> JSON field
    /// for <c>POST /api/v1.3/device</c>. Matches the strings the iOS/macOS
    /// SDK uses (<c>GRDTransportProtocol transportProtocolStringFor</c>):
    /// <c>"ikev2"</c> or <c>"wireguard"</c>. Lowercase by design — distinct
    /// from the registry-format strings (<c>"IKEv2"</c> / <c>"WireGuard"</c>)
    /// used by <see cref="GetPreferred"/> / <see cref="SetPreferred"/>.
    /// </summary>
    public static string TransportProtocolStringFor(TransportProtocol protocol) =>
        protocol switch
        {
            TransportProtocol.TransportWireGuard => "wireguard",
            _                                    => "ikev2",
        };
}
