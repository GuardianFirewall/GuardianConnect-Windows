using System.Runtime.InteropServices;

namespace Win32Calls.WireGuard;

/// <summary>
/// Layout mirror of wireguard.h structs from WireGuardNT 1.1.
///
/// All structs are ALIGNED(8) on the C side. Layout/Size verified against:
///   C:\github\WireGuard\Latest Installers\wireguard-nt-1.1\wireguard-nt\include\wireguard.h
///
/// Expected sizes (assert at startup if you care to be paranoid):
///   WireGuardInterface = 80 bytes
///   WireGuardPeer      = 136 bytes
///   WireGuardAllowedIp = 24 bytes
///   SockAddrInet       = 28 bytes
///
/// Serialization for WireGuardSetConfiguration: a single flat buffer of
///   [WireGuardInterface] [WireGuardPeer + N*WireGuardAllowedIp]*
/// concatenated in declaration order. Build by writing structs sequentially
/// into a pinned byte[] / Marshal.AllocHGlobal block and pass the pointer plus
/// total byte count to WireGuardInterop.WireGuardSetConfiguration.
/// </summary>

internal static class WireGuardConstants
{
    internal const int KeyLength = 32;
}

[Flags]
internal enum WireGuardInterfaceFlags : uint
{
    None = 0,
    HasPublicKey = 1u << 0,
    HasPrivateKey = 1u << 1,
    HasListenPort = 1u << 2,
    ReplacePeers = 1u << 3,
}

[Flags]
internal enum WireGuardPeerFlags : uint
{
    None = 0,
    HasPublicKey = 1u << 0,
    HasPresharedKey = 1u << 1,
    HasPersistentKeepalive = 1u << 2,
    HasEndpoint = 1u << 3,
    // bit 4 intentionally unused in WireGuardNT 1.x
    ReplaceAllowedIPs = 1u << 5,
    Remove = 1u << 6,
    UpdateOnly = 1u << 7,
}

[Flags]
internal enum WireGuardAllowedIpFlags : uint
{
    None = 0,
    Remove = 1u << 0,
}

/// <summary>
/// Address family for the SOCKADDR_INET embedded inside <see cref="WireGuardPeer.Endpoint"/>
/// and for <see cref="WireGuardAllowedIp.AddressFamily"/>. Matches Winsock's AF_* values.
/// </summary>
internal enum WireGuardAddressFamily : ushort
{
    Unspecified = 0,
    InterNetwork = 2,    // AF_INET
    InterNetworkV6 = 23, // AF_INET6
}

/// <summary>
/// Opaque 28-byte blob matching SOCKADDR_INET (union of SOCKADDR_IN/SOCKADDR_IN6).
/// Callers fill <see cref="Bytes"/> directly via Win32 byte ordering:
///   AF_INET (16 of 28 bytes used): family(2 LE) port(2 BE) addr(4 BE) zero(8)
///   AF_INET6 (all 28 bytes):       family(2 LE) port(2 BE) flowinfo(4) addr(16) scope(4)
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 28)]
internal unsafe struct SockAddrInet
{
    public fixed byte Bytes[28];
}

[StructLayout(LayoutKind.Explicit, Size = 80)]
internal unsafe struct WireGuardInterface
{
    [FieldOffset(0)]  public WireGuardInterfaceFlags Flags;
    [FieldOffset(4)]  public ushort ListenPort;
    [FieldOffset(6)]  public fixed byte PrivateKey[WireGuardConstants.KeyLength]; // 6..38
    [FieldOffset(38)] public fixed byte PublicKey[WireGuardConstants.KeyLength];  // 38..70
    [FieldOffset(72)] public uint PeersCount; // 4-byte alignment forces 2 bytes padding before this
    // 76..80 trailing padding (struct ALIGNED(8))
}

[StructLayout(LayoutKind.Explicit, Size = 136)]
internal unsafe struct WireGuardPeer
{
    [FieldOffset(0)]   public WireGuardPeerFlags Flags;
    [FieldOffset(4)]   public uint Reserved;
    [FieldOffset(8)]   public fixed byte PublicKey[WireGuardConstants.KeyLength];     // 8..40
    [FieldOffset(40)]  public fixed byte PresharedKey[WireGuardConstants.KeyLength];  // 40..72
    [FieldOffset(72)]  public ushort PersistentKeepalive;
    // 74..76 implicit padding so Endpoint (alignment 4 via internal DWORDs) starts at 76
    [FieldOffset(76)]  public SockAddrInet Endpoint; // 76..104
    [FieldOffset(104)] public ulong TxBytes;
    [FieldOffset(112)] public ulong RxBytes;
    [FieldOffset(120)] public ulong LastHandshake;
    [FieldOffset(128)] public uint AllowedIPsCount;
    // 132..136 trailing padding (struct ALIGNED(8))
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal unsafe struct WireGuardAllowedIp
{
    [FieldOffset(0)]  public fixed byte Address[16]; // union: V4 in first 4 bytes, V6 across all 16
    [FieldOffset(16)] public WireGuardAddressFamily AddressFamily;
    [FieldOffset(18)] public byte Cidr;
    // 19..20 implicit padding so Flags (alignment 4) starts at 20
    [FieldOffset(20)] public WireGuardAllowedIpFlags Flags;
}
