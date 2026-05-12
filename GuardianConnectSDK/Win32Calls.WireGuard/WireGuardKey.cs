using System.Security.Cryptography;

namespace Win32Calls.WireGuard;

/// <summary>
/// A 32-byte WireGuard key (Curve25519 private/public key or PSK).
/// Constructed from base64 — the wire format used by wg-quick.
///
/// Treat instances as transient secrets when carrying a private key. The byte
/// buffer is owned by this object; callers obtain bytes only via the indexer
/// (for copying into a WIREGUARD_INTERFACE/PEER fixed-size buffer at config-
/// build time) so no Span/Array reference escapes to outside callers.
/// </summary>
public sealed class WireGuardKey
{
    public const int LengthBytes = 32;
    public const int Base64Length = 44; // 32 bytes base64-encoded with trailing '='

    private readonly byte[] _bytes;

    private WireGuardKey(byte[] bytes)
    {
        _bytes = bytes;
    }

    public static WireGuardKey FromBase64(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            throw new FormatException("WireGuard key cannot be empty.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new FormatException("WireGuard key is not valid base64.", ex);
        }

        if (bytes.Length != LengthBytes)
            throw new FormatException(
                $"WireGuard key must decode to {LengthBytes} bytes; got {bytes.Length}.");

        return new WireGuardKey(bytes);
    }

    /// <summary>
    /// Byte access for serialising the key into a WireGuardNT fixed-buffer struct.
    /// Index must be in [0, 32).
    /// </summary>
    internal byte this[int index] => _bytes[index];

    public string ToBase64() => Convert.ToBase64String(_bytes);

    public override string ToString() => "<WireGuardKey>"; // never echo key material via ToString

    /// <summary>
    /// Constant-time comparison. Useful for tests; never used on a hot path.
    /// </summary>
    public bool Equals(WireGuardKey? other)
    {
        if (other is null) return false;
        return CryptographicOperations.FixedTimeEquals(_bytes, other._bytes);
    }
}
