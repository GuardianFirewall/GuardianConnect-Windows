# GuardianConnect SDK (Windows) — 1.4.0 Changelog

_Changes since 1.3.1._

---

## Internal changelog — 1.4.0 (since 1.3.1)

Two features: region selection at country and city precision (GRD-1575), and the Alerts
foundation the app collects against. Package version moves 1.3.1 → 1.4.0 GA.

### Region precision (GRD-1575)

- `v1.3/servers/all-server-regions/{precision}` accepts three precisions and the SDK only ever
  asked for `default`. Both `all-server-regions` and `GetHostsForRegion` now take the precision
  instead of hardcoding `Common.kRegionPrecisionDefault`.
- **`Live.regionLookup` is now keyed by name *and* precision.** The same region name exists at
  more than one precision, so the bare name collides the moment two are live in one session.
  This was the real hazard in the change, not the endpoint plumbing.
- **The connect path was ignoring precision entirely.** `PreferredRegion` is a bare name and
  `SelectGuardianHostWithCompletion` always resolved at `default`, so selecting a city would have
  connected somewhere else. `kPreferredRegionPrecision` is now persisted alongside the region and
  threaded to `SelectBestHostInRegion`. A stored region that no longer resolves at its stored
  precision falls back to the time-zone pick with a warning rather than throwing out of the
  connect flow.
- Time-zone resolution deliberately stays at `default`: all 43 names in the time-zone map resolve
  there, **none** resolve at `city`, and only 26 of 41 at `country` — the missing ones include
  every US region. Automatic therefore needed no change.
- Precision inventory as served: `default` 47 entries (US as 5 compass regions), `city` 57,
  `country` 41. The 41 country entries are a clean 1:1 with the distinct countries in the city
  list, and only five countries have more than one city.
- "Country - Optimal" is a single call, not a fan-out: `hostnames-for-region` with
  `region-precision: country` returns every host across that country, and each host's nested
  region identifies the city it landed in.

### Alerts foundation

- `GRDAlert` model and a v1.4 `GetAlerts`, plus the AOT-safe source-generated JSON context.
- Endpoint version is a non-issue and no migration was needed: `POST /api/v1.2/device/{id}/alerts`
  and the v1.4 path return byte-identical responses.
- `GRDAlert.TimestampUtc` converts a **seconds** wire value via
  `FromUnixTimeMilliseconds((long)(Timestamp * 1000.0))`. Consumers storing the value should store
  milliseconds; a store that persisted the raw seconds renders as January 1970.

### Packaging

- `VersionSuffix` cleared — publishes as **1.4.0**, not `1.4.0-cityprecision.1`. The suffixed
  package stays on the feed and is what the internal 1.4.1 and 1.4.2 app builds consume.

### Validation

- Precisions and counts checked live against connect-api.guardianapp.com, not from the spec.
- Connected end to end over WireGuard from the consuming app: **"USA - Optimal"** resolved
  `na-usa` at country precision across 164 hosts and connected to Seattle from an east-coast
  machine — landing outside the time-zone default is the proof it selected country-wide. A **city
  pick** resolved `us-mia` at city precision across 10 hosts and reconnected to Miami, with the
  change-while-connected disconnect/reconnect running automatically.
- Startup logs confirm both lists load: `RefreshStandbyRegionsForPrecision('country')` 41 regions,
  `('city')` 57.

### Known / deferred

- Multi-hop is **not** implemented. The endpoint it will use is already v1.4, so nothing migrates,
  but the SDK sends no `multihop-exit-region` and `GRDSGWServer` does not parse
  `multihop-entry-enabled` — only 57 of 611 hosts fleet-wide are entry-enabled, so that field is
  required before entry hosts can be filtered.
- All 164 hosts reported `capacity-score: 0` when probed, so "Optimal" picks randomly within the
  lightest tier. Correct per the algorithm, but it will not look deterministic under test.
