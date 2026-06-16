using System;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.IpHelper;
using Windows.Win32.NetworkManagement.Ndis;
using Serilog;

namespace Win32Calls.WFP;

/// <summary>
/// Helpers to find the IF_LUID of the active VPN tunnel adapter post-RASCS_Connected.
///
/// WFP's <c>FWPM_CONDITION_IP_LOCAL_INTERFACE</c> condition wants an IF_LUID, not the
/// classic adapter index returned by <c>GetAdaptersInfo</c>. Once RAS finishes bringing
/// up an IKEv2 tunnel, a WAN Miniport adapter shows up in <c>GetIfTable2</c> with
/// OperStatus=Up; this helper iterates that table and returns the LUID of the most
/// likely candidate.
///
/// Multiple strategies because we don't fully control what Windows names the resulting
/// adapter (depends on Windows version, RAS phonebook contents, and whether multiple
/// concurrent RAS connections exist):
/// 1. Exact alias-match against the RAS entry name.
/// 2. Substring alias-match (in case Windows decorates the alias).
/// 3. Description-substring "WAN Miniport (IKEv2)".
/// 4. Adapter type == IF_TYPE_PPP (23) — what RAS uses for IKEv2 tunnels.
/// </summary>
public static unsafe class AdapterLuidResolver
{
    private const uint IF_TYPE_PPP = 23;

    /// <summary>Try (exact then substring) alias-match against the RAS entry name.</summary>
    public static ulong? FindTunnelLuidByEntryName(string rasEntryName)
    {
        if (string.IsNullOrEmpty(rasEntryName))
        {
            Log.Warning("AdapterLuidResolver.FindTunnelLuidByEntryName: entry name is empty.");
            return null;
        }

        // Exact match first
        var exact = WalkUpAdapters(row =>
        {
            var alias = ReadFixedString(row.Alias.AsSpan());
            return string.Equals(alias, rasEntryName, StringComparison.OrdinalIgnoreCase);
        }, $"alias == '{rasEntryName}' (exact)");
        if (exact != null) return exact;

        // Substring match (e.g., Windows prefixes/suffixes the alias)
        return WalkUpAdapters(row =>
        {
            var alias = ReadFixedString(row.Alias.AsSpan());
            return alias.IndexOf(rasEntryName, StringComparison.OrdinalIgnoreCase) >= 0;
        }, $"alias contains '{rasEntryName}'");
    }

    /// <summary>
    /// Exact alias match on an Up adapter. Used for the WireGuard transport,
    /// whose Wintun adapter is created with a deterministic alias
    /// ("GuardianFirewall-WireGuard") by VpnTunnelManager — distinct from the
    /// RAS-decorated aliases the IKEv2 strategies look for.
    /// </summary>
    public static ulong? FindFirstUpAdapterByAlias(string aliasExact)
    {
        if (string.IsNullOrEmpty(aliasExact)) return null;

        return WalkUpAdapters(row =>
        {
            var alias = ReadFixedString(row.Alias.AsSpan());
            return string.Equals(alias, aliasExact, StringComparison.OrdinalIgnoreCase);
        }, $"alias == '{aliasExact}' (exact, WG)");
    }

    /// <summary>Substring match on the description field of an Up adapter.</summary>
    public static ulong? FindFirstUpAdapterByDescriptionContains(string descriptionSubstring)
    {
        if (string.IsNullOrEmpty(descriptionSubstring)) return null;

        return WalkUpAdapters(row =>
        {
            var description = ReadFixedString(row.Description.AsSpan());
            return description.IndexOf(descriptionSubstring, StringComparison.OrdinalIgnoreCase) >= 0;
        }, $"description contains '{descriptionSubstring}'");
    }

    /// <summary>Match the first Up adapter with the given IF_TYPE (e.g., IF_TYPE_PPP = 23).</summary>
    public static ulong? FindFirstUpAdapterByType(uint ifType)
    {
        return WalkUpAdapters(row => (uint)row.Type == ifType, $"type == {ifType}");
    }

    /// <summary>Convenience wrapper that returns IF_TYPE_PPP (23) — what RAS uses for IKEv2.</summary>
    public static ulong? FindFirstUpPppAdapter() => FindFirstUpAdapterByType(IF_TYPE_PPP);

    /// <summary>
    /// Diagnostic dump of every up adapter. Useful when none of the resolution strategies
    /// match; lets us see in the service log what we were actually presented with.
    /// </summary>
    public static string DumpUpAdapters()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Up adapters (per GetIfTable2):");

        MIB_IF_TABLE2* table = null;
        try
        {
            var result = PInvoke.GetIfTable2(&table);
            if (result != 0 || table == null)
            {
                sb.AppendLine($"  GetIfTable2 failed: 0x{result:X8}");
                return sb.ToString();
            }

            var rowPtr = (MIB_IF_ROW2*)&table->Table;
            var count = 0;
            for (uint i = 0; i < table->NumEntries; i++)
            {
                var row = rowPtr[i];
                if (row.OperStatus != IF_OPER_STATUS.IfOperStatusUp) continue;
                count++;
                sb.AppendLine(
                    $"  ifIndex={row.InterfaceIndex,4} luid=0x{row.InterfaceLuid.Value:X16} type={(uint)row.Type,3} " +
                    $"alias='{ReadFixedString(row.Alias.AsSpan())}' description='{ReadFixedString(row.Description.AsSpan())}'");
            }
            if (count == 0) sb.AppendLine("  (none)");
            return sb.ToString();
        }
        finally
        {
            if (table != null) PInvoke.FreeMibTable(table);
        }
    }

    // ----------------------------------------------------------------------
    // Internal
    // ----------------------------------------------------------------------

    private static ulong? WalkUpAdapters(MibIfRowPredicate predicate, string predicateLabel)
    {
        MIB_IF_TABLE2* table = null;
        try
        {
            var result = PInvoke.GetIfTable2(&table);
            if (result != 0 || table == null)
            {
                Log.Error($"AdapterLuidResolver: GetIfTable2 failed (predicate={predicateLabel}). Error: 0x{result:X8}");
                return null;
            }

            var rowPtr = (MIB_IF_ROW2*)&table->Table;
            for (uint i = 0; i < table->NumEntries; i++)
            {
                var row = rowPtr[i];
                if (row.OperStatus != IF_OPER_STATUS.IfOperStatusUp) continue;
                if (!predicate(row)) continue;

                var luid = row.InterfaceLuid.Value;
                Log.Information(
                    "AdapterLuidResolver: matched on {Predicate} -> ifIndex={Idx} luid=0x{Luid:X16} alias='{Alias}' description='{Description}'",
                    predicateLabel, row.InterfaceIndex, luid,
                    ReadFixedString(row.Alias.AsSpan()),
                    ReadFixedString(row.Description.AsSpan()));
                return luid;
            }

            Log.Information("AdapterLuidResolver: no up adapter matched predicate {Predicate}.", predicateLabel);
            return null;
        }
        finally
        {
            if (table != null) PInvoke.FreeMibTable(table);
        }
    }

    private delegate bool MibIfRowPredicate(MIB_IF_ROW2 row);

    private static string ReadFixedString(ReadOnlySpan<char> chars)
    {
        var nulIndex = chars.IndexOf('\0');
        if (nulIndex < 0) return chars.ToString();
        return chars.Slice(0, nulIndex).ToString();
    }
}
