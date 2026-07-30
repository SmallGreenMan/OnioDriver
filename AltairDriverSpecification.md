# Altair AP-3000 Driver — Specification

**Module:** `AltairAp300.Driver`  
**Class:** `AltairDriver`  
**Namespace:** `AltairAp300.Driver`  
**Protocol tested with FW:** 1.07  
**Revision:** 2026-07-29

---

## Overview

`AltairDriver` is a C# TCP driver for the **Altair AP-3000 Installation Projector**. It provides full state management, command/response matching, automatic reconnection, heartbeat handling, and event-driven feedback. The driver communicates over TCP port **5100** using the AP-3000 ASCII command protocol.

---

## Network Communication

| Parameter | Value |
|---|---|
| Transport | TCP (Ethernet) |
| Default Port | **5100** |
| Character Encoding | ASCII |
| Command Terminator | `;` (semicolon) |
| Response Terminator | `<CR><LF>` (`\r\n`) |
| Max Simultaneous Connections | **1** |
| Logon Credentials | Not required |
| Multi-Connection | Not supported — device returns `!DENY` and closes the socket |

### Protocol Notes (Real-World Observations)

> The following notes were obtained from integration testing with FW 1.07 and supplement the vendor documentation.

- Commands must **not** contain `\r` or `\n` characters. The device accepts the first command that includes a line break but subsequent commands in the same session will be corrupted.
- Multiple commands **may be batched** in a single TCP packet: `SYS:?;SRC:?;LGT:?;` — the device buffers everything and executes commands one by one after receiving each `;` delimiter.
- Device response time is typically **50–270 ms** per command. The driver applies a configurable timeout with retry.
- A small percentage of commands are silently **ignored** by the device. The driver automatically retries up to `CommandRetries` times before performing a recovery reconnect cycle.
- The device supports **only one TCP connection** at a time. A second connection attempt causes the device to respond with `!DENY\r\n` and immediately close the socket.

---

## Instantiation

```csharp
var driver = new AltairDriver(
    ipAddress: "192.168.1.100",  // target IP or hostname
    port: 5100,                   // TCP port (default: 5100)
    commandTimeout: 2,            // seconds per attempt (default: 2)
    commandRetries: 3,            // retries before recovery (default: 3)
    autoReconnect: true,          // auto-reconnect on drop (default: true)
    debug: false                  // console debug output (default: false)
);
```

The optional `client` parameter accepts an `ITcpClient` implementation for dependency injection / unit testing:

```csharp
var driver = new AltairDriver(client: myFakeTcpClient);
```

---

## Configuration & Connection Properties

| Property | Type | Default | Access | Description |
|---|---|---|---|---|
| `IpAddress` | `string` | `"localhost"` | Read | Target IP address or hostname set at construction. |
| `Port` | `int` | `5100` | Read | TCP port set at construction. |
| `CommandTimeout` | `int` | `2` | Read | Per-attempt timeout in seconds for command responses. |
| `CommandRetries` | `int` | `3` | Read | Number of send attempts before triggering a recovery reconnect cycle. |
| `AutoReconnect` | `bool` | `true` | Read / Write | When `true`, the driver automatically reconnects after an unexpected disconnect or connection failure. |
| `Debug` | `bool` | `false` | Read / Write | When `true`, logs all TX/RX traffic and internal events to `Console`. |

---

## Timing & Retry Properties

These properties allow fine-tuning of reconnect and polling behaviour at runtime.

| Property | Type | Default | Description |
|---|---|---|---|
| `AutoReconnectInitialDelaySeconds` | `int` | `1` | Initial delay (s) before the first auto-reconnect attempt after an unexpected disconnect. |
| `AutoReconnectMaxDelaySeconds` | `int` | `60` | Maximum delay (s) between auto-reconnect attempts. Delay grows incrementally up to this cap. |
| `InitialRecoveryReconnectDelaySeconds` | `int` | `2` | Initial delay (s) before reconnecting inside `ConnectAsync` or `SendCommandAsync` recovery loop. |
| `MaxRecoveryReconnectDelaySeconds` | `int` | `60` | Maximum delay (s) for the recovery reconnect loop. |
| `PowerPollingIntervalSeconds` | `int` | `3` | Interval (s) for polling power state while the projector is in a transitional state (SwitchingOn / SwitchingOff). |

---

## State Properties

These properties reflect the last known state of the projector. Values are `null` until the driver has completed the initial state query after connection.  
**States are cached on disconnect** — the last known values are retained after the connection drops.

| Property | Type | Values | Description |
|---|---|---|---|
| `IsConnected` | `bool` | `true` / `false` | Physical TCP connection status. |
| `DeviceIsReady` | `bool` | `true` / `false` | `true` when the initial state query has completed and the driver is fully operational. Resets to `false` on disconnect. |
| `Power` | `PowerState?` | `null` / `Off(0)` / `On(1)` / `SwitchingOn(2)` / `SwitchingOff(3)` | Current power state. `null` = unknown. |
| `Source` | `int?` | `null` / `1`–`4` | Selected input source. `null` = unknown or device is off. |
| `LightOutput` | `int?` | `null` / `0`–`100` | Light output in percent. `null` = unknown. Device returns raw 0–255 value; driver converts to 0–100%. |
| `Shutter` | `bool?` | `null` / `true` / `false` | Shutter state. `true` = closed (blank), `false` = open, `null` = unknown. |
| `FirmwareVersion` | `string` | e.g. `"1.07"` | Firmware version extracted from the `!ID:` greeting on connect. Empty string until first connection. |

### Power State Table

| `PowerState` | Numeric Value | Meaning | Transition Duration |
|---|---|---|---|
| `Off` | `0` | Projector in standby | — |
| `On` | `1` | Projector fully on | — |
| `SwitchingOn` | `2` | Powering on (lamp warming up) | ~8 seconds |
| `SwitchingOff` | `3` | Powering off (lamp cooling down) | ~5 seconds |

> **Note:** While `Power` is `SwitchingOn` or `SwitchingOff`, the driver queues any non-power commands until a stable state (`On` or `Off`) is reached. Power is polled every `PowerPollingIntervalSeconds` during transitions.

> **Note:** The vendor documentation lists power codes 0 and 1 only. Codes 2 (SwitchingOn) and 3 (SwitchingOff) were discovered during integration testing and are not present in the vendor reference.

---

## Events

Events are raised from within the TCP receive loop. Handlers must be exception-safe — all exceptions thrown inside event handlers are caught and logged (when `Debug = true`) but not rethrown.

| Event | Signature | Raised When |
|---|---|---|
| `Connected` | `Action` | TCP connection established. Raised **before** the initial state query. |
| `Disconnected` | `Action` | TCP connection dropped. Raised only when a connection **was previously established** — not on failed connection attempts. |
| `DeviceIsReadyChanged` | `Action<bool>` | `true` when initial state query completes. `false` when disconnected. |
| `PowerStateChanged` | `Action<PowerState?>` | Power state changes (solicited response or unsolicited `!RDY` / `!STBY`). |
| `Ready` | `Action` | Device sends unsolicited `!RDY` — system fully on. |
| `Standby` | `Action` | Device sends unsolicited `!STBY` — system fully off. |
| `SourceStateChanged` | `Action<int?>` | Selected source changes. |
| `LightOutputStateChanged` | `Action<int?>` | Light output percentage changes. |
| `ShutterStateChanged` | `Action<bool?>` | Shutter state changes. |
| `SyncEvent` | `Action<int, int>` | Undocumented `!SYNC:<source>:<status>` message received. Args: source index, status value. |

### Event Subscription Example

```csharp
driver.PowerStateChanged       += state  => Console.WriteLine($"Power: {state}");
driver.Ready                   += ()     => Console.WriteLine("System ON");
driver.Standby                 += ()     => Console.WriteLine("System STANDBY");
driver.DeviceIsReadyChanged    += ready  => Console.WriteLine($"Device ready: {ready}");
driver.SourceStateChanged      += src    => Console.WriteLine($"Source: {src}");
driver.LightOutputStateChanged += lgt    => Console.WriteLine($"Light: {lgt}%");
driver.ShutterStateChanged     += sh     => Console.WriteLine($"Shutter closed: {sh}");
driver.Connected               += ()     => Console.WriteLine("Connected");
driver.Disconnected            += ()     => Console.WriteLine("Disconnected");
driver.SyncEvent               += (src, status) => Console.WriteLine($"Sync src={src} status={status}");
```

---

## Methods

### Connection Management

---

#### `ConnectAsync`

```csharp
Task ConnectAsync(string? host = null, int? port = null, CancellationToken cancellationToken = default)
```

Connects to the projector. On success, waits for the `!ID:` greeting message before returning. If `AutoReconnect = true` and the connection fails or is denied (`!DENY`), retries with incremental backoff up to `MaxRecoveryReconnectDelaySeconds`.

| Parameter | Type | Default | Description |
|---|---|---|---|
| `host` | `string?` | `null` | Override IP/hostname. Falls back to `IpAddress` if null. |
| `port` | `int?` | `null` | Override port. Falls back to `Port` if null. |
| `cancellationToken` | `CancellationToken` | `default` | Cancellation token. |

> If `AutoReconnect = false` and the device returns `!DENY`, an `InvalidOperationException` is thrown.

```csharp
// Basic connect
await driver.ConnectAsync();

// Override address at connect time
await driver.ConnectAsync("10.0.0.50", 5100);
```

---

#### `DisconnectAsync`

```csharp
Task DisconnectAsync()
```

Disconnects from the projector. Cancels any in-progress auto-reconnect. Raises `Disconnected` event. Sets `DeviceIsReady = false`.

```csharp
await driver.DisconnectAsync();
```

---

#### `Disconnect`

```csharp
void Disconnect()
```

Synchronous disconnect. For use in synchronous contexts. Fire-and-forget — does not block on TCP teardown.

```csharp
driver.Disconnect();
```

---

### Control Methods

All control methods return silently on error (logged if `Debug = true`). Invalid parameter values throw `ArgumentOutOfRangeException`.

---

#### `SetPowerAsync`

```csharp
Task SetPowerAsync(bool on, CancellationToken cancellationToken = default)
```

Powers the projector on (`true`) or to standby (`false`). On `ACK`, immediately updates power state to `SwitchingOn` or `SwitchingOff` and begins polling until a stable state is reached.

| `on` | Command Sent | Expected Response |
|---|---|---|
| `true` | `SYS:1;` | `ACK` |
| `false` | `SYS:0;` | `ACK` |

```csharp
await driver.SetPowerAsync(true);   // power on
await driver.SetPowerAsync(false);  // standby
```

---

#### `PowerOnAsync`

```csharp
Task PowerOnAsync(CancellationToken cancellationToken = default)
```

Shortcut for `SetPowerAsync(true)`.

```csharp
await driver.PowerOnAsync();
```

---

#### `PowerOffAsync`

```csharp
Task PowerOffAsync(CancellationToken cancellationToken = default)
```

Shortcut for `SetPowerAsync(false)`.

```csharp
await driver.PowerOffAsync();
```

---

#### `SetSourceAsync`

```csharp
Task SetSourceAsync(int source, CancellationToken cancellationToken = default)
```

Selects input source. After `ACK`, queries and updates `Source`.

| Parameter | Range | Command Sent | Expected Response |
|---|---|---|---|
| `source` | `1`–`4` | `SRC:<n>;` | `ACK` |

> **Note:** Source cannot be changed or queried when the device is off. The device returns `NAK:30` in that case.

```csharp
await driver.SetSourceAsync(2);
```

Throws `ArgumentOutOfRangeException` if `source < 1` or `source > 4`.

---

#### `SetLightOutputAsync`

```csharp
Task SetLightOutputAsync(int value, CancellationToken cancellationToken = default)
```

Sets light output percentage. After `ACK`, queries and updates `LightOutput`.

| Parameter | Range | Command Sent | Expected Response |
|---|---|---|---|
| `value` | `0`–`100` | `LGT:<n>;` | `ACK` |

> **Note:** The device stores light output as a raw 0–255 value. Driver converts on receive: `percent = round(raw * 100 / 255)`.

```csharp
await driver.SetLightOutputAsync(75);
```

Throws `ArgumentOutOfRangeException` if `value < 0` or `value > 100`.

---

#### `SetShutterAsync`

```csharp
Task SetShutterAsync(bool closed, CancellationToken cancellationToken = default)
```

Closes (`true`) or opens (`false`) the lens shutter. After `ACK`, queries and updates `Shutter`.

| `closed` | Command Sent | Expected Response |
|---|---|---|
| `true` | `SHT:1;` | `ACK` |
| `false` | `SHT:0;` | `ACK` |

```csharp
await driver.SetShutterAsync(true);   // blank
await driver.SetShutterAsync(false);  // unblank
```

---

### Query Methods

Query methods send a request command, wait for the response, update the corresponding property, raise its change event if the value changed, and return the current value. On error, the last cached value is returned.

---

#### `QueryPowerAsync`

```csharp
Task<PowerState?> QueryPowerAsync(CancellationToken cancellationToken = default)
```

| Command | Response Format | Property Updated |
|---|---|---|
| `SYS:?;` | `SYS:<n>` | `Power` |

**Return values:**

| Return Value | Meaning |
|---|---|
| `PowerState.Off` (`0`) | Projector is in standby |
| `PowerState.On` (`1`) | Projector is fully on |
| `PowerState.SwitchingOn` (`2`) | Projector is powering on |
| `PowerState.SwitchingOff` (`3`) | Projector is powering off |
| `null` | State unknown (no valid response received; last cached value returned) |

```csharp
PowerState? state = await driver.QueryPowerAsync();
// Example: state == PowerState.On
```

---

#### `QuerySourceAsync`

```csharp
Task<int?> QuerySourceAsync(CancellationToken cancellationToken = default)
```

| Command | Response Format | Property Updated |
|---|---|---|
| `SRC:?;` | `SRC:<n>` | `Source` |

> Returns `NAK:30` from device when off. Driver catches this internally and returns last cached `Source`.

**Return values:**

| Return Value | Meaning |
|---|---|
| `1` | Input 1 selected |
| `2` | Input 2 selected |
| `3` | Input 3 selected |
| `4` | Input 4 selected |
| `null` | Unknown (device off, no response, or not yet queried) |

```csharp
int? source = await driver.QuerySourceAsync();
// Example: source == 2
```

---

#### `QueryLightOutputAsync`

```csharp
Task<int?> QueryLightOutputAsync(CancellationToken cancellationToken = default)
```

| Command | Response Format | Property Updated |
|---|---|---|
| `LGT:?;` | `LGT:<raw 0-255>` | `LightOutput` (0–100%) |

> Device returns a raw 0–255 value. Driver converts using: `percent = round(raw × 100 / 255)`.

**Return values:**

| Return Value | Meaning |
|---|---|
| `0`–`100` | Light output percentage |
| `null` | Unknown (no response received; last cached value returned) |

```csharp
int? light = await driver.QueryLightOutputAsync();
// Example: light == 75
```

---

#### `QueryShutterAsync`

```csharp
Task<bool?> QueryShutterAsync(CancellationToken cancellationToken = default)
```

| Command | Response | Property Updated |
|---|---|---|
| `SHT:?;` | `SHT:1` / `SHT:0` | `Shutter` |

**Return values:**

| Return Value | Meaning |
|---|---|
| `true` | Shutter closed (picture blanked) |
| `false` | Shutter open (picture visible) |
| `null` | Unknown (no response received; last cached value returned) |

```csharp
bool? shutter = await driver.QueryShutterAsync();
// Example: shutter == false  →  picture visible
```

---

#### `QueryAllStatesAsync`

```csharp
Task QueryAllStatesAsync(CancellationToken cancellationToken = default)
```

Sequentially queries all four states in order: Power → Source → LightOutput → Shutter.  
Called automatically in background after every successful connection (on `!ID:` greeting). May also be called manually.

```csharp
await driver.QueryAllStatesAsync();
// After this call: driver.Power, driver.Source, driver.LightOutput, driver.Shutter are populated.
```

---

## Unsolicited Messages (Device Push)

The device sends the following messages without any prior command:

| Message | Trigger | Driver Action |
|---|---|---|
| `!ID:AP-3000:<fw>` | On every TCP connect | Extracts `FirmwareVersion`, unblocks `ConnectAsync`, triggers `QueryAllStatesAsync` in background |
| `!RDY` | System fully powered on | Sets `Power = On`, raises `PowerStateChanged`, raises `Ready`, queries `SRC:?;` in background |
| `!STBY` | System fully in standby | Sets `Power = Off`, raises `PowerStateChanged`, raises `Standby` |
| `!HB` | Heartbeat request (~every 20–25 s) | Driver responds `HB;` within 5 s. Missed 2× HB → device sends `!DROP` and closes socket |
| `!DROP` | Heartbeat timeout | Device closes connection. Driver handles per `AutoReconnect` setting |
| `!DENY` | Second client tries to connect | Exception in `ConnectAsync`. If `AutoReconnect = true`, retries with backoff |
| `!SYNC:<src>:<status>` | Undocumented push when system is on | Raises `SyncEvent(src, status)` |

> **Heartbeat:** `!HB` arrives every 20–25 seconds. After receiving `!HB`, there is a **5-second window** to respond with `HB;`. Regular commands do **not** reset the heartbeat timer.

---

## Error Codes

| Code | Meaning | Driver Behaviour |
|---|---|---|
| `NAK:10` | Unrecognized command | Throws `AltairNakException` (code `"10"`) |
| `NAK:20` | Parameter out of range | Throws `AltairNakException` (code `"20"`) |
| `NAK:30` | Command unavailable when device is off | Throws `AltairNakException` (code `"30"`), caught internally in query/set methods |

`AltairNakException` extends `InvalidOperationException` and exposes `NakCode` (`string`) and a human-readable `Message`.

---

## Connection Lifecycle

```
ConnectAsync()
    │
    ├─► TCP connect
    │       │
    │       ├─ OK ──► wait for !ID: greeting
    │       │              │
    │       │              ├─ received ──► fire Connected
    │       │              │              ──► QueryAllStatesAsync (background)
    │       │              │              ──► fire DeviceIsReadyChanged(true)
    │       │              │              ──► ConnectAsync returns ✓
    │       │              │
    │       │              └─ timeout / !DENY ──► DisconnectAsync ──► retry (if AutoReconnect)
    │       │
    │       └─ FAIL ──► retry (if AutoReconnect) with incremental backoff
    │
    │   [connected — normal operation]
    │
    ├─► Heartbeat loop: device sends !HB every ~20–25s
    │       driver responds HB; within 5s
    │
    ├─► On unexpected disconnect (ReadLoop: null line / socket error):
    │       ──► fire Disconnected
    │       ──► fire DeviceIsReadyChanged(false)
    │       ──► StartAutoReconnect (if AutoReconnect && !manual)
    │
    └─► DisconnectAsync() / Disconnect()
            ──► cancel reconnect loop
            ──► fire DeviceIsReadyChanged(false)
            ──► fire Disconnected
```

---

## Full Integration Example

```csharp
using AltairAp300.Driver;

using var driver = new AltairDriver(
    ipAddress: "192.168.1.100",
    port: 5100,
    commandTimeout: 2,
    commandRetries: 3,
    autoReconnect: true,
    debug: true
);

driver.Connected               += () => Console.WriteLine("Connected");
driver.Disconnected            += () => Console.WriteLine("Disconnected");
driver.DeviceIsReadyChanged    += ready => Console.WriteLine($"DeviceReady: {ready}");
driver.PowerStateChanged       += state => Console.WriteLine($"Power: {state}");
driver.Ready                   += () => Console.WriteLine("!RDY — system on");
driver.Standby                 += () => Console.WriteLine("!STBY — system standby");
driver.SourceStateChanged      += src => Console.WriteLine($"Source: {src}");
driver.LightOutputStateChanged += lgt => Console.WriteLine($"LightOutput: {lgt}%");
driver.ShutterStateChanged     += sh => Console.WriteLine($"Shutter closed: {sh}");
driver.SyncEvent               += (src, st) => Console.WriteLine($"Sync src={src} st={st}");

await driver.ConnectAsync();

Console.WriteLine($"FW: {driver.FirmwareVersion}");
Console.WriteLine($"Power: {driver.Power}");
Console.WriteLine($"Source: {driver.Source}");
Console.WriteLine($"LightOutput: {driver.LightOutput}%");
Console.WriteLine($"Shutter: {driver.Shutter}");

// Control
await driver.PowerOnAsync();
await driver.SetSourceAsync(2);
await driver.SetLightOutputAsync(80);
await driver.SetShutterAsync(false);

// Query
PowerState? power   = await driver.QueryPowerAsync();
int?        source  = await driver.QuerySourceAsync();
int?        light   = await driver.QueryLightOutputAsync();
bool?       shutter = await driver.QueryShutterAsync();

await driver.DisconnectAsync();
```

---

## Disposal

`AltairDriver` implements `IDisposable`. Always dispose when done to release TCP resources, cancel reconnect loops, and stop polling tasks.

```csharp
using var driver = new AltairDriver(...);
// or
driver.Dispose();
```

---

## Appendix A. Full Protocol Reference

All known commands and device messages for the Altair AP-3000.  
Source: vendor reference rev. 2.4 + real-world integration testing with FW 1.07.

> **Legend:**  
> ✅ Vendor documented · 🔬 Observed in testing · ⚠️ Observed but undocumented by vendor

### A.1 Host → Device Commands

| Command | Direction | Description | Response (success) | Response (error) | Notes |
|---|---|---|---|---|---|
| `SYS:1;` | TX | Power on | `ACK` | `NAK:10` / `NAK:20` | ✅ Works regardless of power state |
| `SYS:0;` | TX | Power off (standby) | `ACK` | `NAK:10` / `NAK:20` | ✅ Works regardless of power state |
| `SYS:?;` | TX | Query power state | `SYS:<n>` | `NAK:10` | ✅ Returns 0, 1, 2 or 3 |
| `SRC:<n>;` | TX | Select input source (1–4) | `ACK` | `NAK:10` / `NAK:20` / `NAK:30` | ✅ Requires device on (`NAK:30` if off) |
| `SRC:?;` | TX | Query selected source | `SRC:<n>` | `NAK:10` / `NAK:30` | ✅ Returns `NAK:30` if device is off |
| `LGT:<n>;` | TX | Set light output (0–100) | `ACK` | `NAK:10` / `NAK:20` | ✅ Sends percent; device stores as 0–255 raw |
| `LGT:?;` | TX | Query light output | `LGT:<raw>` | `NAK:10` | ✅ Returns raw 0–255 (driver converts to %) |
| `SHT:1;` | TX | Close shutter (blank) | `ACK` | `NAK:10` / `NAK:20` | ✅ |
| `SHT:0;` | TX | Open shutter (unblank) | `ACK` | `NAK:10` / `NAK:20` | ✅ |
| `SHT:?;` | TX | Query shutter state | `SHT:1` / `SHT:0` | `NAK:10` | ✅ |
| `HB;` | TX | Heartbeat response | *(none)* | *(none)* | 🔬 Must be sent within 5 s of receiving `!HB` |

### A.2 Device → Host Responses

| Message | Trigger | Values | Notes |
|---|---|---|---|
| `ACK` | Successful command execution | — | ✅ Generic acknowledgement |
| `SYS:<n>` | Response to `SYS:?;` | `0` Off · `1` On · `2` SwitchingOn · `3` SwitchingOff | ✅ Values 0–1 vendor documented; 2–3 🔬 observed |
| `SRC:<n>` | Response to `SRC:?;` | `1`–`4` | ✅ |
| `LGT:<n>` | Response to `LGT:?;` | Raw `0`–`255` | ✅ Raw range; driver exposes as 0–100% |
| `SHT:1` | Response to `SHT:?;` | — | ✅ Shutter closed |
| `SHT:0` | Response to `SHT:?;` | — | ✅ Shutter open |
| `NAK:10` | Unrecognized command | — | ✅ |
| `NAK:20` | Parameter out of range | — | ✅ |
| `NAK:30` | Command unavailable when device is off | — | 🔬 Not in vendor excerpt; observed on `SRC:?;` / `SRC:<n>;` when off |

### A.3 Device → Host Unsolicited Messages

| Message | Trigger | Notes |
|---|---|---|
| `!ID:AP-3000:<fw>` | Sent immediately on every TCP connect | 🔬 E.g. `!ID:AP-3000:1.07`. FW version is the third colon-delimited field. |
| `!RDY` | System finished powering on | 🔬 ⚠️ Not in vendor excerpt. Follows power-on cycle (~8 s after `SYS:1;`). Driver automatically issues `SRC:?;` after receiving this message |
| `!STBY` | System finished powering off | 🔬 ⚠️ Not in vendor excerpt. Follows power-off cycle (~5 s after `SYS:0;`) |
| `!HB` | Heartbeat ping (~every 20–25 s) | 🔬 Device expects `HB;` response within 5 s |
| `!DROP` | Heartbeat timeout (missed 2 consecutive HBs) | 🔬 Device sends this then closes the TCP socket |
| `!DENY` | Second client tried to connect | 🔬 Device sends this then closes the TCP socket |
| `!SYNC:<src>:<status>` | Internal state change while device is on | ⚠️ Undocumented. E.g. `!SYNC:4:1`, `!SYNC:3:0`. Meaning of fields unknown. |

### A.4 Power State Codes

| Code | `PowerState` | Meaning | Stable? | Transition Duration |
|---|---|---|---|---|
| `0` | `Off` | Projector in standby | ✅ Yes | — |
| `1` | `On` | Projector fully operational | ✅ Yes | — |
| `2` | `SwitchingOn` | Lamp warming up | ❌ Transient | ~8 seconds |
| `3` | `SwitchingOff` | Lamp cooling down | ❌ Transient | ~5 seconds |

> Codes 2 and 3 are not listed in the vendor documentation (rev. 2.4) but are consistently returned by the device during power transitions (FW 1.07).

### A.5 NAK Error Codes

| NAK Code | Meaning | Vendor Documented |
|---|---|---|
| `10` | Unrecognized command | ✅ |
| `20` | Parameter out of range | ✅ |
| `30` | Command is available only when device is on | 🔬 Observed only |

### A.6 Protocol Behaviour Summary

| Behaviour | Detail |
|---|---|
| Command delimiter | `;` — device buffers input and executes on each `;` |
| Response terminator | `<CR><LF>` (`\r\n`) |
| In-band line breaks | Must **not** be used inside commands — corrupts subsequent messages in session |
| Command batching | Multiple commands in one TCP packet accepted: `SYS:?;SRC:?;LGT:?;` |
| Response timing | 50–270 ms per command |
| Silent command drops | Device silently ignores a small percentage of commands — driver retries automatically |
| Concurrent connections | Only **1** client allowed simultaneously |
| Heartbeat interval | `!HB` every 20–25 s; `HB;` must be sent within 5 s |
| Heartbeat miss tolerance | 2 missed heartbeats → `!DROP` + socket close |
| Heartbeat timer reset | **Only** `HB;` resets the timer; other commands do not |
| Source / LightOutput when off | `SRC:?;`, `SRC:<n>;` return `NAK:30`. LightOutput is queryable but not changeable |

