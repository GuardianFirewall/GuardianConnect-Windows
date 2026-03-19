# TestGRDConnectObjects

Console test app that exercises `GRDConnectSubscriber`, `GRDConnectDevice`, and the
underlying `GRDHousekeepingAPI` methods in a fixed sequence. All inputs come from
`testconfig.json` (copied to the output directory on build) and from whatever subscriber
/ device state is currently stored in the registry.

---

## testconfig.json fields

| Field | Type | Default | Purpose |
|---|---|---|---|
| `connectApiHostname` | string | `connect-api.guardianapp.com` | Passed to `GRDVPNHelper.Singleton.ConnectAPIHostname` |
| `subscriberIdentifier` | string | `""` | Used when no stored subscriber exists — passed to `RegisterNewConnectSubscriberAsync` |
| `subscriberSecret` | string | `""` | Always re-applied to the loaded/registered subscriber at startup (secret is not kept in the registry) |
| `subscriberEmail` | string | `""` | Email for new subscriber registration |
| `deviceNickname` | string | `"TestDevice"` | Nickname for the primary device registered in Step 1 |
| `acceptedTOS` | bool | `true` | Passed as `acceptedTOS` wherever required |
| `countOfAdditionalDevicesToCreate` | int | `0` | Number of extra devices created in Step 4. Set > 0 to enable. |
| `newEmail` | string | `""` | Set a non-empty value to enable Step 10 (UpdateConnectSubscriberWithEmailAddressAsync) |
| `newDeviceNickname` | string | `""` | Set a non-empty value to enable Step 9 (UpdateConnectDeviceNicknameAsync) |
| `runLogout` | bool | `false` | Set `true` to run the optional LogoutConnectSubscriberAsync step |
| `runDestroy` | bool | `false` | Set `true` to run the optional DestroySubscriber step — clears all stored state |

---

## Test sequence

### Step 1 — `GetCurrentSubscriber` → `RegisterNewConnectSubscriberAsync` → `CheckGuardianAccountStateAsync`

Tries to load a previously stored `GRDConnectSubscriber` from the registry, then
registers a new subscriber if none is found, and finally runs `CheckGuardianAccountStateAsync`
in both cases to confirm the account is known to the backend.

- **Stored subscriber found:** loads it, captures `device` from `subscriber.Device`
  (populated internally by `GetCurrentSubscriber`). Re-applies `subscriberSecret` from
  config before calling `CheckGuardianAccountStateAsync`.
- **No stored subscriber:** builds a stub from `subscriberIdentifier` / `subscriberSecret`
  / `subscriberEmail` in config and calls `RegisterNewConnectSubscriberAsync(acceptedTOS,
  deviceNickname)`. On success the subscriber, first device, and PE-Token are stored. If
  the registration response includes a device, `device` is captured from
  `subscriber.Device`. If registration fails the run stops.

After whichever branch succeeds, `CheckGuardianAccountStateAsync` is called on the
resulting subscriber. The `subscriberSecret` from config is re-applied before the call
because `Store()` clears it.

SDK methods: `GRDConnectSubscriber.GetCurrentSubscriber`,
`GRDConnectSubscriber.RegisterNewConnectSubscriberAsync`,
`GRDConnectSubscriber.CheckGuardianAccountStateAsync`
Housekeeping API: `AddNewConnectSubscriberAsync` (#185), `CheckAccountCreationStateAsync` (#190)

---

### Step 2 — `GetCurrentSubscriber` / `RegisterNewConnectSubscriberAsync`

Confirms the subscriber is present in the registry. This handles the case where Step 1
used the `CheckGuardianAccountStateAsync` path and only has a stub subscriber in memory
with no locally stored state.

- **Subscriber found in registry:** loads the fully-populated subscriber and re-applies
  `subscriberSecret` from config. Also captures `device` from `subscriber.Device` if
  present.
- **Subscriber not in registry:** calls `RegisterNewConnectSubscriberAsync` using config
  credentials. On success stores the subscriber and captures any device returned in the
  response. If registration fails the run stops.

SDK methods: `GRDConnectSubscriber.GetCurrentSubscriber`,
`GRDConnectSubscriber.RegisterNewConnectSubscriberAsync`
Housekeeping API: `AddNewConnectSubscriberAsync` (#185)

---

### Step 3 — `ValidateConnectSubscriberAsync`

Uses the subscriber confirmed/created in Step 2 to validate the subscription against
the backend using the current PE-Token. On success a fresh PE-Token is stored and the
updated `subscriber` (with populated SKU, expiry, etc.) replaces the Step 2 instance.
The `subscriberSecret` from config is immediately re-applied because `Store()` clears it
and `InitFromDictionary` does not restore it.
**The run stops here if this step fails** — subsequent steps depend on a valid PE-Token.

SDK method: `GRDConnectSubscriber.ValidateConnectSubscriberAsync`
Housekeeping API: `ValidateConnectSubscriberAsync` (#188)

---

### Step 4 — `ConnectDeviceReferenceAsync`

Retrieves the device record associated with the current PE-Token from the backend.
The returned `GRDConnectDevice` (UUID, Nickname) is kept in the `device` variable used
by later steps.

SDK method: `GRDConnectSubscriber.ConnectDeviceReferenceAsync`
Housekeeping API: `GetDeviceReferenceForConnectSubscriberAsync` (#186)

---

### Step 4 — `GRDConnectDevice.AddConnectDeviceAsync` (additional devices)

Creates `countOfAdditionalDevicesToCreate` extra devices using the current PE-Token.
Each extra device is named `{device.Nickname}-Extra-{N}` where N starts at 2 (device 1
is the primary device already registered in Step 1).

Example — if `deviceNickname` is `"MyBox"` and `countOfAdditionalDevicesToCreate` is `3`:

| Extra # | Nickname created |
|---|---|
| 2 | `MyBox-Extra-2` |
| 3 | `MyBox-Extra-3` |
| 4 | `MyBox-Extra-4` |

Skipped when `countOfAdditionalDevicesToCreate` is `0` (default).

SDK method: `GRDConnectDevice.AddConnectDeviceAsync`
Housekeeping API: `AddConnectDeviceAsync` (#191)

---

### Step 5 — `AllDevicesAsync`

Lists all devices associated with the subscriber. Each device's UUID, Nickname, and
`IsCurrentDevice` flag is printed.

SDK method: `GRDConnectSubscriber.AllDevicesAsync`
Housekeeping API: `RequestAllConnectDevicesForSubscriberAsync` (#193)

---

### Step 6 — `GRDConnectDevice.GetCurrentDevice`

Reads the current device back from the registry (the device stored during registration /
validation).

SDK method: `GRDConnectDevice.GetCurrentDevice`

---

### Step 7 — `ValidateConnectDeviceAsync`

Validates the current device against the backend. Skipped automatically if no device or
no PE-Token is found in the registry.

SDK method: `GRDConnectDevice.ValidateConnectDeviceAsync`
Housekeeping API: `ValidateConnectDeviceAsync` (#195)

---

### Step 8 — `GRDConnectDevice.ListConnectDevicesForPETokenAsync`

Lists all devices visible to the current PE-Token and prints each one. Skipped if no
PE-Token is present.

SDK method: `GRDConnectDevice.ListConnectDevicesForPETokenAsync`
Housekeeping API: `RequestAllConnectDevicesForSubscriberAsync` (#193)

---

### Step 9 — `UpdateConnectDeviceNicknameAsync`  _(optional)_

Renames the current device to `newDeviceNickname`.
**Skipped** when `newDeviceNickname` is empty (default).

SDK method: `GRDConnectDevice.UpdateConnectDeviceNicknameAsync`
Housekeeping API: `UpdateConnectDeviceAsync` (#192)

---

### Step 10 — `UpdateConnectSubscriberWithEmailAddressAsync`  _(optional)_

Updates the subscriber's email address to `newEmail`.
**Skipped** when `newEmail` is empty (default).

SDK method: `GRDConnectSubscriber.UpdateConnectSubscriberWithEmailAddressAsync`
Housekeeping API: `UpdateConnectSubscriberWithEmailAsync` (#187)

---

### Optional — `LogoutConnectSubscriberAsync`

Logs out the subscriber (invalidates the PE-Token on the backend).
**Skipped** unless `runLogout: true` is set. Run this last if you want to test the
logout path without destroying local state.

SDK method: `GRDConnectSubscriber.LogoutConnectSubscriberAsync`
Housekeeping API: `LogOutConnectSubscriberAsync`

---

### Optional — `DestroySubscriber`

Removes all subscriber and PE-Token data from the registry/keychain. After this step
the next run will treat the machine as a fresh install and attempt registration again.
**Skipped** unless `runDestroy: true` is set.

SDK method: `GRDConnectSubscriber.DestroySubscriber`

---

## Output conventions

Every step prints one of:

```
OK   — <success detail>
FAIL — <error message>  GrdApiError=<api error code>
```

Steps that are skipped due to missing config print an `Information`-level message
explaining which config field to set.