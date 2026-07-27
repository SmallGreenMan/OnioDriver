# Altair AP-3000 Installation Projector — Control Protocol (excerpt)

> Excerpt from *AP-3000 Series Integrator Reference, rev. 2.4*.
> Protocol was tested with FW - 1.07





## 7.4 External Control

The AP-3000 can be controlled via RS-232 (3-pin Phoenix connector, 19200 8N1)
or via Ethernet (TCP port **5100**). The command set is identical on both
transports.

Commands are ASCII strings terminated with a semicolon `;`. Line breaks may be
inserted between commands for readability.
The projector terminates its messages with `<CR><LF>`.
The projector acknowledges every command with either the requested value,
`ACK`, or an error code.

## 7.5 Command Reference

| Command    | Description                        | Response            |
|------------|------------------------------------|---------------------|
| `SYS:1;`   | Power the projector on             | `ACK`               |
| `SYS:0;`   | Power the projector off (standby)  | `ACK`               |
| `SYS:?;`   | Query power state                  | `SYS:1` / `SYS:0`   |
| `SRC:<n>;` | Select source 1–4                  | `ACK`               |
| `SRC:?;`   | Query selected source              | `SRC:<n>`           |
| `LGT:<n>;` | Set light output, 0–100            | `ACK`               |
| `LGT:?;`   | Query light output (0–100)         | `LGT:<n>`           |
| `SHT:1;`   | Close the shutter (blank picture)  | `ACK`               |
| `SHT:0;`   | Open the shutter                   | `ACK`               |
| `SHT:?;`   | Query shutter state                | `SHT:1` / `SHT:0`   |

## 7.6 Error Codes

| Code     | Meaning                |
|----------|------------------------|
| `NAK:10` | Unrecognized command   |
| `NAK:20` | Parameter out of range |

*(remaining pages of this chapter are not available)*
