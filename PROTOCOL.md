# Altair AP-3000 Installation Projector — Control Protocol (Real-World Notes)

> Empirical observations from integration testing, supplementing the vendor
> reference (`AP-3000_Protocol_Excerpt.md`).

## Command Framing

- Commands must **not** contain `\r` or `\n`. The first command containing a
  line break is accepted, but every subsequent command in the same session is
  corrupted.
- Multiple commands may be batched in a single TCP packet, e.g. `SYS:?;SRC:?;LGT:?;`
- The device buffers everything it receives and executes commands one by one,
  only after it receives the `;` delimiter for each.
- Device response time is typically **50–270 ms** per command.
- The device silently **ignores a small percentage of commands** — if no
  response arrives, the driver retries the command.

## Connection

- On connect, the device sends `!ID:AP-3000:1.07` (probably the FW version).
- The device supports **only one TCP connection at a time**. If a second
  client attempts to connect, the device responds `!DENY\r\n` and closes the
  connection.

## Power State

- `SYS:?;` reports the power state as one of:
  - `0` — device off
  - `1` — device on
  - `2` — device switching on (~8 sec)
  - `3` — device switching off (~5 sec)

- Unsolicited push messages report power transitions completing:
  - `!RDY\r\n` — sent when the system finishes powering on.
  - `!STBY\r\n` — sent when the system finishes powering off.

## Source & Light Output

- `SRC:?;` returns `NAK:30\r\n` if the device is off.
- Source **cannot be read or changed** while the device is off.
- `LGT:?;` reports light output as a raw **0–255** value, but the set command
  (`LGT:<n>;`) takes a **0–100** value.

## Heartbeat

- The device sends `!HB` roughly every **20–25 sec**.
- The driver must respond with `HB;` within **5 sec** of receiving `!HB`.
- `HB;` sent unprompted (before a `!HB` is received) is ignored, and `!HB`
  itself expects no other response than `HB;`.
- No command other than `HB;` resets the heartbeat timer.
- If **2 consecutive heartbeats** are missed, the device sends `!DROP\r\n` and
  closes the connection.

## Undocumented Push Messages

- While the device is on, it may send unsolicited, undocumented messages:
  - `!SYNC:<source>:<status>`, e.g. `!SYNC:4:1`, `!SYNC:3:0`

  Meaning of the fields is unknown.

# Full Protocol Reference

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


