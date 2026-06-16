// SPDX-License-Identifier: BSD-3-Clause
//
// Curve25519 keypair generation for the WireGuard credential key-exchange
// path. Compiled with `go build -buildmode=c-shared` to produce a
// stand-alone curve25519.dll that exports two C-ABI entry points matching
// the WireGuardKit framework's iOS/macOS API:
//
//   void curve25519_generate_private_key(unsigned char private_key[32]);
//   void curve25519_derive_public_key(unsigned char public_key[32],
//                                     const unsigned char private_key[32]);
//
// Implementation uses the Go standard library: crypto/ecdh.X25519 for the
// scalar multiplication and crypto/rand.Reader for the CSPRNG (which on
// Windows backs onto BCryptGenRandom). License chain is BSD-3-Clause all
// the way down — Go's standard library is BSD-3-Clause, and this wrapper
// is BSD-3-Clause too. No GPL anywhere.
//
// Build (see build.cmd in the parent directory):
//   GOOS=windows GOARCH=amd64 CGO_ENABLED=1 go build -buildmode=c-shared
//   GOOS=windows GOARCH=arm64 CGO_ENABLED=1 go build -buildmode=c-shared

package main

import "C"

import (
	"crypto/ecdh"
	"crypto/rand"
	"unsafe"
)

// Both exported functions take a *byte (a pointer to a 32-byte buffer
// owned by the caller) rather than the more idiomatic *[32]byte —
// cgo's //export does not accept fixed-size array types as parameters.
// We use unsafe.Slice to materialise a length-32 Go slice over the
// caller's buffer for the copies below. The C-side ABI is unchanged:
// `void f(unsigned char buf[32])` is just `void f(unsigned char *buf)`.

// curve25519_generate_private_key fills the 32-byte buffer at private_key
// with a Curve25519 private scalar drawn from the system CSPRNG (Windows
// BCryptGenRandom under crypto/rand). On any failure (rare; e.g. RNG
// hardware unavailable) the buffer is zeroed so callers can detect the
// all-zero key as an error sentinel.
//
//export curve25519_generate_private_key
func curve25519_generate_private_key(privateKey *byte) {
	out := unsafe.Slice(privateKey, 32)
	curve := ecdh.X25519()
	priv, err := curve.GenerateKey(rand.Reader)
	if err != nil {
		for i := range out {
			out[i] = 0
		}
		return
	}
	copy(out, priv.Bytes())
}

// curve25519_derive_public_key computes the public key associated with
// private_key and writes the 32-byte result into public_key. The private
// key buffer is read as a 32-byte little-endian scalar; crypto/ecdh's
// X25519 implementation handles the clamping internally. On any failure
// (invalid private-key length etc.) the public_key buffer is zeroed.
//
//export curve25519_derive_public_key
func curve25519_derive_public_key(publicKey *byte, privateKey *byte) {
	pubOut := unsafe.Slice(publicKey, 32)
	privIn := unsafe.Slice(privateKey, 32)
	curve := ecdh.X25519()
	priv, err := curve.NewPrivateKey(privIn)
	if err != nil {
		for i := range pubOut {
			pubOut[i] = 0
		}
		return
	}
	pub := priv.PublicKey()
	copy(pubOut, pub.Bytes())
}

// main is required for buildmode=c-shared but is never executed when the
// DLL is loaded by a consumer process.
func main() {}
