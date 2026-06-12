# Guardian Connect SDK — 0.46.0 Changelog

**Range:** changes since the prior `main` baseline **`0.46.0-alpha.2`** (last merge PR #220, *anycpu-consolidation*) up to and including the **`0.46.0`** release (PR #222, branch `i196-add-wireguard-transport`). 75 commits.
**Tags:** `0.46.0`, `0.46.0-WG`.

This release lands **two major features together — the WireGuard transport and the VPN Kill Switch** — plus the supporting WFP DNS-leak protection, credential-negotiation rework, and a long tail of reliability fixes developed across the `0.46.0-wg-alpha.1` … `0.46.0-wg-alpha.41` prerelease line.

---

## WireGuard transport (new)
- New `Win32Calls.WireGuard` P/Invoke layer over WireGuardNT; vendored `wireguard.dll` (win-x64 + win-arm64).
- `VpnTunnelManager`: adapter create/configure/bring-up, IP/DNS/route attachment, tunnel interface-metric pinning.
- Dynamic credential negotiation — client-side Curve25519 keypair + `POST /api/v1.3/device` — wired into `ConnectVpnWithConfiguredCredentials`.
- Transport selection by user preference; service-side dispatcher routes IKEv2 vs WireGuard by request shape.
- Host enumeration and per-host override on negotiate; honors `kGuardianPreferredHost` (incl. on `_hostLookup` cache miss).
- WireGuard connection state made observable to the client; duplicate-Connect requests rejected rather than silently replaced.

## VPN Kill Switch (new)
- `KillSwitchFilters` WFP wrapper: default-deny block-all with a weighted permit stack (LAN, DNS, tunnel adapter, transport).
- `KillSwitchService` orchestrator: event-driven RAS state tracking (replaced 1 Hz polling), suspend/resume handling, restores user-intent state on startup, and is WireGuard-aware.
- IPC contract + dispatcher + `ClientPipe` passthroughs; HKLM state publish for cross-process auto-refresh; status-changed named event for UI refresh.
- Transport permits: **Allow LAN** (private/link-local/multicast ranges), IKEv2 (UDP 500/4500), IPSec tunnel (IP-in-IP proto 4, ESP proto 50); ICMP gap closed.
- Permit the WireGuard encrypted carrier under the kill switch (fixes "kill switch on + WireGuard = no internet").

## DNS-leak protection (WFP)
- DNS-leak permit/block filter set installed on WireGuard connect; LUID-keyed permit filters (renamed `TunnelDnsPermit`).
- Fixed the IKEv2 DNS-leak filter pipeline (latent bug) and post-disconnect DNS-resolution failures.

## Cryptography / provenance
- Replaced the Curve25519 implementation (was GPL-2.0 OR MIT) with a Go **BSD-3** wrapper around `crypto/ecdh.X25519`.
- `curve25519.dll` now built in CI for clean provenance, compiled with static CRT (`/MT`), Go bumped to 1.25; added to the SignPath deep-sign filelist (x64 + arm64).

## Reliability & fixes
- Serialized all `ClientPipe` writes through `_pipeIO`; fixed `ReadStringAsync` short-read and bounded `SendVoidCommand` response read (UI hang); drain the service-startup ACK in `ReopenNamedPipe`.
- async-void hardening so transient HTTP failures don't crash the UI; kill-switch "rock-and-hard-place" recovery + `ClientPipeService` self-heal.
- Mitigated a WireGuardNT `0xCE` BSOD via a post-Dispose quiet period.
- Region-cache resilience on failed refresh; error-aware geo refresh + re-solicit + swap gate; non-throwing region lookup.
- Fixed WireGuard Disconnect pipe-protocol race and stopped waking the IKEv2 poller spuriously.

## Architecture / refactors
- Process-wide transport singleton + Exception serialization across the pipe.
- Unified credentials check + symmetric protocol dispatch; symmetric `GetServerStatus`; `ClearVpnConfiguration` made awaitable (`async Task`).
- Consolidated the transport-protocol enum + registry I/O (`TransportProtocolStringFor` moved into `GRDTransportProtocol`).
- Realigned registry roots: HKCU user settings under `GuardianFirewall\Settings`, HKLM service-broadcast values under `Software\GuardianFirewall`.
- Code-comment cleanup: role aliases instead of personal names.

## Versioning
- `0.46.0-wg-alpha.1` … `0.46.0-wg-alpha.41` prerelease line promoted to **`0.46.0`** stable (`VersionSuffix` cleared in `GuardianConnectSDK/Directory.Build.Props`; base `Version` was already `0.46.0`).
- This is the SDK revision (`7e6cb4d`) consumed by the app `0.40.43-alpha.5` tested build.
