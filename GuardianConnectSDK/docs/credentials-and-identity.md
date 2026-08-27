# Credentials and Identity

Four different things are called "credentials" in this codebase and they are not
interchangeable. This document separates them, shows how each is obtained, where
each is stored, and what invalidates it.

## Source of truth

| Concern | Component | File |
|---|---|---|
| Credential store | `GRDCredentialManager` | `GuardianConnect/Credentials/GRDCredentialManager.cs` |
| Encrypted storage | `GRDKeychain` / `IGRDKeychain` | `GuardianConnect/Credentials/GRDKeychain.cs` |
| Encryption | `DPAPI` | `GuardianConnect/Win32/DPAPI.cs` |
| Device credential | `GRDCredential`, `VPNDeviceResponse` | `GuardianConnect/Credentials/` |
| Subscriber credential | `GRDSubscriberCredential` | `GuardianConnect/Credentials/GRDSubscriberCredential.cs` |
| PE token | `GRDPEToken` | `GuardianConnect/Credentials/GRDPEToken.cs` |
| wg-quick builder | `GRDWireGuardConfiguration` | `GuardianConnect/Credentials/GRDWireGuardConfiguration.cs` |
| Registry paths | `RegistrySettings` | `Shared/RegistrySettings.cs` |

## The four artefacts

```mermaid
classDiagram
    class GRDPEToken {
        +string Token
        +expiry / subscription info
        note "proves the human logged in"
    }
    class GRDSubscriberCredential {
        +string JWT
        +subscription type
        note "signed assertion of entitlement"
    }
    class GRDCredential {
        +string HostName
        +string ApiAuthToken
        +string EapUsername
        +string EapPassword
        +string DevicePrivateKey
        +string DevicePublicKey
        +GRDSGWServer Device
        +TransportProtocol
        note "per-device, per-host, per-transport"
    }
    class GRDWireGuardConfiguration {
        <<builder>>
        +WireGuardQuickConfigForCredential(...)$ string
        note "renders wg-quick text; not stored"
    }

    GRDPEToken --> GRDSubscriberCredential : mints
    GRDSubscriberCredential --> GRDCredential : authorises creation
    GRDCredential --> GRDWireGuardConfiguration : input
```

| Artefact | Answers | Lifetime |
|---|---|---|
| **PE token** | "which account is this?" | until logout or expiry |
| **Subscriber credential** | "is this account entitled?" | short-lived JWT, minted per need |
| **Device credential** | "how does this device dial this host?" | until invalidated or host/transport changes |
| **wg-quick config** | "what does WireGuardNT need right now?" | built per connect, never stored |

The device credential is the one people mean by "credentials" in bug reports. It
is bound to a **specific host** and a **specific transport**, which is why
switching either one invalidates it.

## Acquisition

```mermaid
sequenceDiagram
    autonumber
    participant UI as Client app
    participant HK as GRDHousekeepingAPI
    participant GW as GRDGateway (chosen SGW host)
    participant CM as GRDCredentialManager
    participant KC as GRDKeychain → DPAPI

    UI->>HK: LoginUserWithEmail(email, password)
    HK-->>UI: PE token
    UI->>KC: store PE token
    Note over UI,KC: HKCU\Software\GuardianFirewall\Settings

    UI->>HK: POST /api/v1.2/subscriber-credential/create
    HK-->>UI: subscriber credential (JWT)

    UI->>GW: POST /api/v1.4/device-credentials<br/>{subscriber-credential, transport-protocol, public-key?}
    GW-->>UI: VPNDeviceResponse<br/>client-id, mapped IPv4/IPv6, server public key,<br/>EAP username/password (IKEv2)
    UI->>CM: AddOrUpdateCredential(GRDCredential)
    CM->>KC: StoreData(...) → DPAPI.Encrypt(UserKey)
```

WireGuard generates its keypair **client-side** and sends only the public key;
the private key never leaves the machine. IKEv2 receives EAP username/password
from the gateway. That asymmetry is why validity is checked per transport.

## Validity

`ActiveConnectionPossible(protocol)` is protocol-aware — a credential valid for
one transport is not valid for the other:

```mermaid
flowchart TD
    A["ActiveConnectionPossible(protocol)"] --> B{"stored credential?"}
    B -->|no| X["false — establish new"]
    B -->|yes| C{"protocol"}
    C -->|IKEv2| D{"ApiAuthToken<br/>+ EapUsername<br/>+ EapPassword<br/>+ HostName"}
    C -->|WireGuard| E{"DevicePrivateKey + DevicePublicKey<br/>+ ServerPublicKey<br/>+ MappedIPv4 + ClientId<br/>+ HostName"}
    D -->|all present| Y["true — reuse"]
    E -->|all present| Y
    D -->|any missing| X
    E -->|any missing| X
```

A historical trap worth knowing: `GRDCredential` stuffs backwards-compatible
`UserName`/`Password` values onto WireGuard credentials (`UserName = ClientId`,
`Password = "wireguard-creds"`). Before the transport switch began clearing
credentials unconditionally, that let `ActiveConnectionPossible(IKEv2)` return
true for a WireGuard credential — and the dispatcher would then feed those values
to `RasDial`, which returned `ERROR_AUTH_INTERNAL` (645).

## Storage and encryption

Everything persists under **`HKCU\Software\GuardianFirewall\Settings`**, encrypted
with DPAPI.

```mermaid
flowchart LR
    subgraph app["Client app process (user)"]
        CM["GRDCredentialManager"] --> KC["GRDKeychain"]
        KC --> DP["DPAPI.Encrypt / Decrypt<br/>KeyType.UserKey"]
    end
    DP --> REG["HKCU\Software\GuardianFirewall\Settings<br/>GuardianCredentialsList<br/>pe-token-tokenitself<br/>subscriber-credential"]
    HKLM["HKLM\Software\GuardianFirewall<br/>KillSwitchMode · AllowLan<br/>ShowDeveloperWindow"] -.->|not encrypted,<br/>machine scope| SVC["service"]
```

`DPAPI` supports `KeyType.UserKey` and `KeyType.MachineKey`; the default is
**`UserKey`**, and `CRYPTPROTECT_UI_FORBIDDEN` is always set so encryption never
prompts.

Two consequences that have already cost real debugging time:

**Only the same Windows user can decrypt.** Credentials written by one account
are unreadable by another. Anything running as a different user sees an empty or
failing store.

**The logon session must carry the user's credentials.** DPAPI unwraps the user's
master key from their password. A network-type logon that has no password
material — SSH **public-key** authentication is the common case — cannot decrypt,
and `CryptProtectData`/`CryptUnprotectData` fail with `Access is denied`. Running
a tool over SSH pubkey auth on a machine where the app works fine will still fail
here. Use an interactive session, or a logon created with credentials.

## Invalidation

| Trigger | Effect |
|---|---|
| Transport switch | credential cleared unconditionally, then re-established |
| Host or region change | new host means a new device credential |
| Reset Configuration | device registration cleared; region selection is **kept** (platform parity) |
| Clear PE Token (dev) | login cleared; device credential untouched |
| `invalidate-credentials` | gateway-side invalidation for that client id |

`Reset Configuration` deliberately does **not** reset the selected region — that
matches the other platforms.

## Endpoints involved

| Purpose | Endpoint |
|---|---|
| Login | `POST /api/v1/users/…` (housekeeping) |
| PE token info | `GET /api/v1/users/info-for-pe-token` |
| Subscriber credential | `POST /api/v1.2/subscriber-credential/create` |
| Device credential | `POST /api/v1.4/device-credentials` (gateway host) |
| Verify | `POST /api/v1.4/device/{clientId}/verify-credentials` |
| Invalidate | `POST /api/v1.4/device/{clientId}/invalidate-credentials` |

See [`backend-api.md`](./backend-api.md) for the full endpoint surface and
versioning.
