using System.Runtime.InteropServices;

namespace Win32Calls.WireGuard;

/// <summary>
/// Direct P/Invoke surface for wireguard.dll (WireGuardNT 1.1+).
///
/// All functions match the C signatures from include/wireguard.h. Higher-level
/// orchestration (config serialization, key handling, lifecycle) belongs in the
/// caller (VpnTunnelManager), not here.
///
/// AOT note: [LibraryImport] over [DllImport] because the service publishes with
/// PublishAot=true and PublishSingleFile=true. wireguard.dll is deployed
/// side-by-side via the consolidated nuspec (runtimes/win-{x64,arm64}/native/);
/// it is not embedded.
/// </summary>
internal static partial class WireGuardInterop
{
    private const string WireGuardDll = "wireguard.dll";

    /// <summary>
    /// Creates a new WireGuard adapter. Returns NULL (IntPtr.Zero) on failure;
    /// call Marshal.GetLastWin32Error for details. Pass requestedGuid = null to
    /// let the driver pick a GUID.
    /// </summary>
    [LibraryImport(WireGuardDll, EntryPoint = "WireGuardCreateAdapter", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static unsafe partial nint WireGuardCreateAdapter(string name, string tunnelType, Guid* requestedGuid);

    /// <summary>
    /// Opens an existing WireGuard adapter by name. Returns NULL (IntPtr.Zero)
    /// if no adapter with that name exists.
    /// </summary>
    [LibraryImport(WireGuardDll, EntryPoint = "WireGuardOpenAdapter", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint WireGuardOpenAdapter(string name);

    /// <summary>
    /// Releases the adapter handle. If the adapter was created (not just opened),
    /// the underlying adapter is destroyed.
    /// </summary>
    [LibraryImport(WireGuardDll, EntryPoint = "WireGuardCloseAdapter")]
    internal static partial void WireGuardCloseAdapter(nint adapter);

    /// <summary>
    /// Retrieves the IF_LUID of the WireGuard adapter — the value to feed into
    /// FWP_CONDITION_IP_LOCAL_INTERFACE for LUID-keyed WFP filters.
    /// </summary>
    [LibraryImport(WireGuardDll, EntryPoint = "WireGuardGetAdapterLUID")]
    internal static partial void WireGuardGetAdapterLUID(nint adapter, out ulong luid);

    /// <summary>
    /// Applies a configuration buffer: WIREGUARD_INTERFACE struct followed by
    /// N x WIREGUARD_PEER (each followed by M x WIREGUARD_ALLOWED_IP), all
    /// concatenated. Caller owns the buffer lifetime.
    /// </summary>
    [LibraryImport(WireGuardDll, EntryPoint = "WireGuardSetConfiguration", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WireGuardSetConfiguration(nint adapter, nint config, uint bytes);

    /// <summary>
    /// Reads back the current configuration into the provided buffer. The
    /// 'bytes' parameter is in/out: pass the buffer capacity in, get the
    /// bytes-written out. If too small, returns false and 'bytes' carries the
    /// required size (Marshal.GetLastWin32Error = ERROR_MORE_DATA).
    /// </summary>
    [LibraryImport(WireGuardDll, EntryPoint = "WireGuardGetConfiguration", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WireGuardGetConfiguration(nint adapter, nint config, ref uint bytes);

    /// <summary>
    /// Brings the adapter administratively up or down. Up requires a prior
    /// successful WireGuardSetConfiguration with at least one peer.
    /// </summary>
    [LibraryImport(WireGuardDll, EntryPoint = "WireGuardSetAdapterState", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WireGuardSetAdapterState(nint adapter, WireGuardAdapterState state);

    [LibraryImport(WireGuardDll, EntryPoint = "WireGuardGetAdapterState", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WireGuardGetAdapterState(nint adapter, out WireGuardAdapterState state);
}

internal enum WireGuardAdapterState : uint
{
    Down = 0,
    Up = 1,
}
