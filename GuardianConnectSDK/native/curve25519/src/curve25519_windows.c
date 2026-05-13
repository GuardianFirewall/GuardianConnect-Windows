// SPDX-License-Identifier: GPL-2.0 OR MIT
//
// Windows-side wrappers around the upstream wireguard-tools curve25519
// implementation. Exposes the same two-function API as the macOS/iOS
// WireGuardKit framework (`x25519.h`):
//
//   void curve25519_generate_private_key(unsigned char private_key[32]);
//   void curve25519_derive_public_key(unsigned char public_key[32],
//                                     const unsigned char private_key[32]);
//
// The underlying field arithmetic and scalar multiplication come from
// curve25519.c / curve25519-fiat32.h / curve25519-hacl64.h (MIT
// Fiat-Crypto / HACL*), unmodified from wireguard-tools upstream.
// Randomness comes from BCryptGenRandom (CNG) so we don't ship our own
// CSPRNG.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <bcrypt.h>
#include <stdint.h>

#include "curve25519.h"

#pragma comment(lib, "bcrypt.lib")

__declspec(dllexport)
void curve25519_generate_private_key(unsigned char private_key[32])
{
    // BCryptGenRandom with BCRYPT_USE_SYSTEM_PREFERRED_RNG draws from
    // the kernel CSPRNG. NTSTATUS == 0 (STATUS_SUCCESS) on success.
    NTSTATUS status = BCryptGenRandom(
        NULL,
        (PUCHAR)private_key,
        (ULONG)32,
        BCRYPT_USE_SYSTEM_PREFERRED_RNG);

    if (status != 0) {
        // No safe way to recover here. Zero the buffer so callers can't
        // accidentally use predictable bytes, and abort. The C# wrapper
        // will detect the all-zero key and surface an error.
        for (int i = 0; i < 32; i++) private_key[i] = 0;
        return;
    }

    // Curve25519 clamp: clear the three low bits of byte 0, clear the
    // high bit of byte 31, set the second-high bit of byte 31. Matches
    // curve25519_clamp_secret() in curve25519.h.
    private_key[0]  &= 248;
    private_key[31]  = (private_key[31] & 127) | 64;
}

__declspec(dllexport)
void curve25519_derive_public_key(unsigned char public_key[32],
                                  const unsigned char private_key[32])
{
    curve25519_generate_public(public_key, private_key);
}
