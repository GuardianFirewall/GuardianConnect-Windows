using System.Runtime.InteropServices;

namespace Win32Calls.WireGuard;

/// <summary>
/// Direct P/Invoke surface for curve25519.dll, the small native library
/// that wraps wireguard-tools' upstream curve25519 (Fiat-Crypto on 32-bit,
/// HACL* on 64-bit — both MIT-derived formally-verified implementations).
///
/// Exposes the same two-function API as the iOS/macOS WireGuardKit
/// framework header <c>x25519.h</c>:
///
///   void curve25519_generate_private_key(unsigned char[32]);
///   void curve25519_derive_public_key(unsigned char[32], const unsigned char[32]);
///
/// Randomness for <c>curve25519_generate_private_key</c> comes from
/// <c>BCryptGenRandom(BCRYPT_USE_SYSTEM_PREFERRED_RNG)</c> inside the
/// native wrapper — i.e. the Windows kernel CSPRNG.
///
/// AOT note: [LibraryImport] over [DllImport], to match the rest of this
/// project's AOT publish profile.
/// </summary>
internal static partial class Curve25519Interop
{
    private const string Curve25519Dll = "curve25519.dll";

    /// <summary>
    /// Fill the 32-byte buffer with a freshly-generated, clamped Curve25519
    /// private key. Internally: BCryptGenRandom → low 3 bits of byte 0 cleared,
    /// high bit of byte 31 cleared, second-high bit of byte 31 set.
    ///
    /// If the CSPRNG call fails, the buffer is zeroed (so callers can detect
    /// the failure by checking for all-zero and refuse to proceed).
    /// </summary>
    [LibraryImport(Curve25519Dll, EntryPoint = "curve25519_generate_private_key")]
    internal static partial void GeneratePrivateKey(Span<byte> privateKey);

    /// <summary>
    /// Derive the 32-byte public key for a Curve25519 private key via scalar
    /// multiplication against the standard basepoint (=9).
    /// </summary>
    [LibraryImport(Curve25519Dll, EntryPoint = "curve25519_derive_public_key")]
    internal static partial void DerivePublicKey(Span<byte> publicKey, ReadOnlySpan<byte> privateKey);
}
