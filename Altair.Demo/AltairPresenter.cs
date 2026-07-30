using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AltairAp300.Driver;

namespace Altair.Demo;

/// <summary>
/// Bridges a UI application to <see cref="AltairDriver"/> over a WebSocket server.
/// Clients send JSON commands to control the projector and receive JSON events
/// (state changes, connection status) in real time over the same connection.
/// </summary>
public sealed class AltairPresenter : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AltairDriver _driver;
    private readonly HttpListener _httpListener;
    private readonly int _wsPort;
    private readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new();

    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    public AltairPresenter(string deviceIpAddress, int devicePort, int wsPort = 10001)
    {
        _wsPort = wsPort;
        _driver = new AltairDriver(deviceIpAddress, devicePort) { Debug = true };
        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add($"http://localhost:{wsPort}/");

        SubscribeToDriverEvents();
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _httpListener.Start();
        Console.WriteLine($"[PRESENTER] WebSocket server listening on ws://localhost:{_wsPort}/");

        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);

        // Connect in the background so the WebSocket server is usable immediately even if the
        // device is unreachable at startup (AltairDriver.ConnectAsync retries until it succeeds).
        _ = ConnectDriverAsync(_cts.Token);

        return Task.CompletedTask;
    }

    private async Task ConnectDriverAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _driver.ConnectAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PRESENTER] Initial driver connection failed: {ex.Message}");
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _httpListener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (Exception)
            {
                break;
            }

            _ = HandleConnectionAsync(context, cancellationToken);
        }
    }

    private async Task HandleConnectionAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.Close();
            return;
        }

        WebSocket socket;
        try
        {
            var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
            socket = wsContext.WebSocket;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PRESENTER] WebSocket handshake failed: {ex.Message}");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.Close();
            return;
        }

        var clientId = Guid.NewGuid();
        var client = new ClientConnection(socket);
        _clients[clientId] = client;
        Console.WriteLine($"[PRESENTER] Client connected: {clientId}");

        try
        {
            await SendAsync(client, new EventMessage("event", "StateSnapshot", BuildStateSnapshot()), cancellationToken);
            await ReceiveLoopAsync(client, cancellationToken);
        }
        finally
        {
            _clients.TryRemove(clientId, out _);
            Console.WriteLine($"[PRESENTER] Client disconnected: {clientId}");
        }
    }

    private async Task ReceiveLoopAsync(ClientConnection client, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (client.Socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var messageStream = new System.IO.MemoryStream();
            WebSocketReceiveResult result;
            try
            {
                do
                {
                    result = await client.Socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", cancellationToken);
                        return;
                    }
                    messageStream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
            }
            catch (Exception)
            {
                return;
            }

            var json = Encoding.UTF8.GetString(messageStream.ToArray());
            await HandleCommandAsync(client, json, cancellationToken);
        }
    }

    private async Task HandleCommandAsync(ClientConnection client, string json, CancellationToken cancellationToken)
    {
        CommandMessage? command;
        try
        {
            command = JsonSerializer.Deserialize<CommandMessage>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            await SendAsync(client, new ResponseMessage("error", null, null, $"Invalid JSON: {ex.Message}"), cancellationToken);
            return;
        }

        if (command is null || string.IsNullOrWhiteSpace(command.Command))
        {
            await SendAsync(client, new ResponseMessage("error", command?.RequestId, null, "Missing 'command' field"), cancellationToken);
            return;
        }

        try
        {
            object? data = command.Command.ToLowerInvariant() switch
            {
                "connect" => await ExecuteAsync(() => _driver.ConnectAsync(command.Ip, command.Port, cancellationToken)),
                "disconnect" => await ExecuteAsync(() => _driver.DisconnectAsync()),
                "poweron" => await ExecuteAsync(() => _driver.PowerOnAsync(cancellationToken)),
                "poweroff" => await ExecuteAsync(() => _driver.PowerOffAsync(cancellationToken)),
                "setsource" => await ExecuteAsync(() => _driver.SetSourceAsync(RequireInt(command), cancellationToken)),
                "setlightoutput" => await ExecuteAsync(() => _driver.SetLightOutputAsync(RequireInt(command), cancellationToken)),
                "setshutter" => await ExecuteAsync(() => _driver.SetShutterAsync(RequireBool(command), cancellationToken)),
                "querypower" => await ExecuteAsync(() => _driver.QueryPowerAsync(cancellationToken)),
                "querysource" => await ExecuteAsync(() => _driver.QuerySourceAsync(cancellationToken)),
                "querylightoutput" => await ExecuteAsync(() => _driver.QueryLightOutputAsync(cancellationToken)),
                "queryshutter" => await ExecuteAsync(() => _driver.QueryShutterAsync(cancellationToken)),
                "queryall" => await ExecuteAsync(() => _driver.QueryAllStatesAsync(cancellationToken)),
                "getstate" => BuildStateSnapshot(),
                _ => throw new ArgumentException($"Unknown command '{command.Command}'")
            };

            await SendAsync(client, new ResponseMessage("response", command.RequestId, data, null), cancellationToken);
        }
        catch (Exception ex)
        {
            await SendAsync(client, new ResponseMessage("error", command.RequestId, null, ex.Message), cancellationToken);
        }
    }

    private async Task<object?> ExecuteAsync(Func<Task> action)
    {
        await action();
        return BuildStateSnapshot();
    }

    private static int RequireInt(CommandMessage command) =>
        command.IntValue ?? throw new ArgumentException($"'intValue' is required for command '{command.Command}'");

    private static bool RequireBool(CommandMessage command) =>
        command.BoolValue ?? throw new ArgumentException($"'boolValue' is required for command '{command.Command}'");

    private object BuildStateSnapshot() => new
    {
        isConnected = _driver.IsConnected,
        deviceIsReady = _driver.DeviceIsReady,
        power = _driver.Power?.ToString(),
        source = _driver.Source,
        lightOutput = _driver.LightOutput,
        shutter = _driver.Shutter,
        firmwareVersion = _driver.FirmwareVersion,
        ipAddress = _driver.IpAddress,
        port = _driver.Port
    };

    private void SubscribeToDriverEvents()
    {
        _driver.PowerStateChanged += state => Broadcast("PowerStateChanged", new { power = state?.ToString() });
        _driver.SourceStateChanged += src => Broadcast("SourceStateChanged", new { source = src });
        _driver.LightOutputStateChanged += val => Broadcast("LightOutputStateChanged", new { lightOutput = val });
        _driver.ShutterStateChanged += shutter => Broadcast("ShutterStateChanged", new { shutter });
        _driver.Connected += () => Broadcast("Connected", null);
        _driver.Disconnected += () => Broadcast("Disconnected", null);
        _driver.Ready += () => Broadcast("Ready", null);
        _driver.Standby += () => Broadcast("Standby", null);
        _driver.DeviceIsReadyChanged += ready => Broadcast("DeviceIsReadyChanged", new { deviceIsReady = ready });
        _driver.SyncEvent += (source, status) => Broadcast("SyncEvent", new { source, status });
    }

    private void Broadcast(string eventName, object? data)
    {
        var message = new EventMessage("event", eventName, data);
        foreach (var (id, client) in _clients)
        {
            _ = SendAsync(client, message, CancellationToken.None).ContinueWith(t =>
            {
                if (t.IsFaulted) _clients.TryRemove(id, out _);
            }, TaskScheduler.Default);
        }
    }

    private static async Task SendAsync(ClientConnection client, object message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);

        await client.SendLock.WaitAsync(cancellationToken);
        try
        {
            if (client.Socket.State == WebSocketState.Open)
            {
                await client.Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }
        }
        finally
        {
            client.SendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();

        if (_httpListener.IsListening) _httpListener.Stop();
        _httpListener.Close();

        foreach (var client in _clients.Values)
        {
            try
            {
                await client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None);
            }
            catch
            {
                // Ignore close errors during shutdown
            }
        }
        _clients.Clear();

        if (_acceptLoopTask != null)
        {
            try { await _acceptLoopTask; } catch { /* Ignore - loop was cancelled */ }
        }

        _driver.Dispose();
        _cts?.Dispose();
    }

    private sealed class ClientConnection(WebSocket socket)
    {
        public WebSocket Socket { get; } = socket;
        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }

    private sealed record CommandMessage(
        string Command,
        string? Ip = null,
        int? Port = null,
        int? IntValue = null,
        bool? BoolValue = null,
        string? RequestId = null);

    private sealed record ResponseMessage(string Type, string? RequestId, object? Data, string? Error);

    private sealed record EventMessage(string Type, string Event, object? Data);
}
