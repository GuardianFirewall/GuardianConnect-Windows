using System;
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
/// v1 strategy: match on the alias (which RAS sets to the entry name) + OperStatus=Up.
/// If multiple matches exist, prefer the most recently up one. Returns null if none
/// found.
/// </summary>
public static unsafe class AdapterLuidResolver
{
    /// <summary>
    /// Find the IF_LUID of the active tunnel adapter for a given RAS entry name. RAS
    /// names the WAN Miniport adapter after the entry name when the connection comes up,
    /// so the alias field in MIB_IF_ROW2 should match (case-insensitively, exact).
    /// </summary>
    /// <param name="rasEntryName">The RAS entry name (e.g., "Guardian Firewall - us-east").</param>
    /// <returns>The IF_LUID value if a matching up-state adapter is found; null otherwise.</returns>
    public static ulong? FindTunnelLuidByEntryName(string rasEntryName)
    {
        if (string.IsNullOrEmpty(rasEntryName))
        {
            Log.Warning("AdapterLuidResolver.FindTunnelLuidByEntryName: entry name is empty.");
            return null;
        }

        MIB_IF_TABLE2* table = null;
        try
        {
            var result = PInvoke.GetIfTable2(&table);
            if (result != 0 || table == null)
            {
                Log.Error($"AdapterLuidResolver.FindTunnelLuidByEntryName: GetIfTable2 failed. Error: 0x{result:X8}");
                return null;
            }

            // Walk the variable-length Table[] in MIB_IF_TABLE2 (inline-array struct field).
            var rowPtr = (MIB_IF_ROW2*)&table->Table;
            for (uint i = 0; i < table->NumEntries; i++)
            {
                var row = rowPtr[i];
                if (row.OperStatus != IF_OPER_STATUS.IfOperStatusUp) continue;

                var alias = ReadFixedString(row.Alias.AsSpan());
                if (string.Equals(alias, rasEntryName, StringComparison.OrdinalIgnoreCase))
                {
                    var luid = row.InterfaceLuid.Value;
                    Log.Information(
                        "AdapterLuidResolver: matched RAS entry '{Entry}' to adapter '{Alias}' (LUID 0x{Luid:X16}, ifIndex={Idx}).",
                        rasEntryName, alias, luid, row.InterfaceIndex);
                    return luid;
                }
            }

            Log.Warning(
                "AdapterLuidResolver.FindTunnelLuidByEntryName: no up-state adapter alias matched '{Entry}'.",
                rasEntryName);
            return null;
        }
        finally
        {
            if (table != null) PInvoke.FreeMibTable(table);
        }
    }

    /// <summary>
    /// Fallback: find the first up-state adapter whose description contains a given
    /// substring (case-insensitive). Useful when the alias-match path fails — RAS's
    /// description usually contains "WAN Miniport (IKEv2)" or similar.
    /// </summary>
    public static ulong? FindFirstUpAdapterByDescriptionContains(string descriptionSubstring)
    {
        if (string.IsNullOrEmpty(descriptionSubstring)) return null;

        MIB_IF_TABLE2* table = null;
        try
        {
            var result = PInvoke.GetIfTable2(&table);
            if (result != 0 || table == null)
            {
                Log.Error($"AdapterLuidResolver.FindFirstUpAdapterByDescriptionContains: GetIfTable2 failed. Error: 0x{result:X8}");
                return null;
            }

            var rowPtr = (MIB_IF_ROW2*)&table->Table;
            for (uint i = 0; i < table->NumEntries; i++)
            {
                var row = rowPtr[i];
                if (row.OperStatus != IF_OPER_STATUS.IfOperStatusUp) continue;

                var description = ReadFixedString(row.Description.AsSpan());
                if (description.IndexOf(descriptionSubstring, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var luid = row.InterfaceLuid.Value;
                    Log.Information(
                        "AdapterLuidResolver: matched description '{Description}' (LUID 0x{Luid:X16}, ifIndex={Idx}).",
                        description, luid, row.InterfaceIndex);
                    return luid;
                }
            }

            Log.Warning(
                "AdapterLuidResolver.FindFirstUpAdapterByDescriptionContains: no up-state adapter description matched '{Sub}'.",
                descriptionSubstring);
            return null;
        }
        finally
        {
            if (table != null) PInvoke.FreeMibTable(table);
        }
    }

    private static string ReadFixedString(ReadOnlySpan<char> chars)
    {
        // Inline-array char buffers in MIB_IF_ROW2 are zero-padded to fixed length.
        var nulIndex = chars.IndexOf('\0');
        if (nulIndex < 0) return chars.ToString();
        return chars.Slice(0, nulIndex).ToString();
    }
}
