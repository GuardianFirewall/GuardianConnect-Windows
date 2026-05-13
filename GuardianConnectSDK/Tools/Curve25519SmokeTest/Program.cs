using Win32Calls.WireGuard;

// Three things we want to confirm:
//   1. GeneratePrivateKey returns a non-zero key.
//   2. The clamp bits are correct (low 3 bits of byte 0 cleared, top bit of
//      byte 31 cleared, second-top bit of byte 31 set).
//   3. DerivePublicKey is deterministic for a fixed private key, and the
//      published RFC 7748 test vector reproduces.

int failures = 0;

void Check(string name, bool ok, string detail = "")
{
    var tag = ok ? "PASS" : "FAIL";
    Console.WriteLine($"  [{tag}] {name}{(detail.Length > 0 ? " — " + detail : "")}");
    if (!ok) failures++;
}

// --- 1: fresh key isn't zero -------------------------------------------------
Console.WriteLine("Generating fresh private key...");
var priv = WireGuardKey.GeneratePrivateKey();
var privB64 = priv.ToBase64();
Console.WriteLine($"  base64 = {privB64}");
Check("private key is 44-char base64", privB64.Length == 44);

// --- 2: clamp bits -----------------------------------------------------------
// Round-trip through base64 to read the raw bytes back.
var privBytes = Convert.FromBase64String(privB64);
Check("byte[0] low 3 bits cleared",
    (privBytes[0] & 0x07) == 0,
    $"byte[0] = 0x{privBytes[0]:X2}");
Check("byte[31] high bit cleared",
    (privBytes[31] & 0x80) == 0,
    $"byte[31] = 0x{privBytes[31]:X2}");
Check("byte[31] second-high bit set",
    (privBytes[31] & 0x40) == 0x40,
    $"byte[31] = 0x{privBytes[31]:X2}");

// --- 3: derive public key from this private key ------------------------------
var pub = priv.DerivePublicKey();
var pubB64 = pub.ToBase64();
Console.WriteLine($"\nDerived public key:");
Console.WriteLine($"  base64 = {pubB64}");
Check("public key is 44-char base64", pubB64.Length == 44);

// Determinism: derive twice, expect identical result.
var pub2 = priv.DerivePublicKey();
Check("public key derivation is deterministic", pub.ToBase64() == pub2.ToBase64());

// --- 4: RFC 7748 §6.1 test vector -------------------------------------------
// Alice's private key (already clamped per the RFC).
var aliceB64 = Convert.ToBase64String(Convert.FromHexString(
    "77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a"));
var alicePub = WireGuardKey.FromBase64(aliceB64).DerivePublicKey();
var expected = Convert.ToBase64String(Convert.FromHexString(
    "8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a"));
Check("RFC 7748 Alice public matches",
    alicePub.ToBase64() == expected,
    alicePub.ToBase64());

Console.WriteLine();
Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
return failures == 0 ? 0 : 1;
