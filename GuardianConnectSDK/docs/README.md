# GuardianConnect Windows SDK — Design Documentation

Architecture and design reference for the SDK: the assemblies, the service
runtime, the IPC contract, transports, credentials, the backend API, and the
firewall filters.

**Start with [`architecture-overview.md`](./architecture-overview.md).** It has
the process split, the assembly graph and the deployment layout, and it names the
one thing that is not obvious from the source tree: the Windows service project
is a shell, and everything it runs lives in this SDK.

## Contents

| Document | Covers |
|---|---|
| [`architecture-overview.md`](./architecture-overview.md) | Process and privilege split, assemblies and NuGet packages, deployment layout |
| [`ipc-contract.md`](./ipc-contract.md) | Named-pipe wire format, the 17 commands, concurrency, framing failure modes |
| [`service-runtime.md`](./service-runtime.md) | The three hosted services, startup, power and network events, IKEv2 notification fragility |
| [`transports.md`](./transports.md) | `ITransportProvider`, selection, connection state, disconnect, WireGuard/IKEv2 asymmetry |
| [`tunnel-creation-sequences.md`](./tunnel-creation-sequences.md) | The connect path step by step, common and per-protocol |
| [`credentials-and-identity.md`](./credentials-and-identity.md) | PE token, subscriber credential, device credential, DPAPI storage, invalidation |
| [`backend-api.md`](./backend-api.md) | Housekeeping vs gateway, endpoint versions, region precision, host selection |
| [`kill-switch-and-dns.md`](./kill-switch-and-dns.md) | The two WFP filter sets, modes, connecting overlay, LUID resolution, lifetime |

Client app, installer and update documentation lives in the **guardianapp-windows**
repo under `WorkProgression/docs/`.

## Conventions

Diagrams are **Mermaid**, rendered inline by GitHub. Component and deployment
views are drawn as grouped flowcharts rather than strict UML notation, which
Mermaid does not provide.

Each document opens with a source-of-truth table naming the files it describes.
Documents cite files rather than restating logic, so that code changes surface as
stale references rather than silently wrong prose.

`Win32Calls` and `Win32Calls.WFP` are frozen generated P/Invoke — roughly 101,000
lines — and appear only as an opaque boundary. Their contents are not documented.

## Reading order for a new developer

1. `architecture-overview.md` — the shape of the system
2. `ipc-contract.md` — the one channel between the two processes
3. `tunnel-creation-sequences.md` — what happens when someone clicks Connect
4. Then whichever subsystem you are working in
