# Altair AP-3000 — Demo

Demo application for the Altair AP-3000 projector driver (`AltairAp300.Driver`). It has two run modes, switched by a single constant in `Altair.Demo/Program.cs`.

## 1. Set the device IP

The projector's IP address and port are passed as command-line arguments when running `Altair.Demo`:

```
dotnet run --project Altair.Demo -- <ip> [port]
```

- `<ip>` — the projector's IP address or hostname (defaults to `localhost` if the argument is omitted).
- `[port]` — TCP port (defaults to `5100`).

Example:

```
dotnet run --project Altair.Demo -- 192.168.1.100 5100
```

The defaults can also be changed directly in `Altair.Demo/Program.cs` (`ipAddress`/`port`).

## 2. Pick a mode — `DebugRect` in `Program.cs`

Near the top of `Program.cs` there's a constant:

```csharp
const bool DebugRect = true;
```

### `DebugRect = true` — demo with the React UI

The app starts `AltairPresenter` — a WebSocket server on `ws://localhost:10001/` that forwards commands to the driver and broadcasts state events in real time. It doesn't print anything on its own — everything is driven through the WebSocket.

Steps:
1. Start the driver/presenter: `dotnet run --project Altair.Demo -- <ip> [port]`
2. In a separate terminal, start the React app (`Altair.Demo.React/AltairDemoReactApp`):
   ```
   npm install
   npm run dev
   ```
3. Open the address Vite prints (usually `http://localhost:5173`) and control the projector from the UI.

The React client connects to `ws://localhost:10001/` by default (see `src/services/presenter/index.ts`; override with `VITE_PRESENTER_WS_URL`).

### `DebugRect = false` — demo as a console application

Plain console scenario, no WebSocket: the app connects to the projector itself and runs through power/source/light/shutter commands in sequence, printing the result of each step and every driver event to the console.

Run:
```
dotnet run --project Altair.Demo -- <ip> [port]
```

## Further documentation

- `AltairDriverSpecification.md` — full driver specification (properties, events, methods, device protocol).
- 'PROTOCOL.md' - real device protocol bihavior
- `.github/workflows/ci.yml` — CI pipeline (build / run / test), runs on PRs and manually (`workflow_dispatch`).
