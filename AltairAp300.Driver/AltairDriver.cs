using System;
using System.Threading;
using System.Threading.Tasks;

namespace AltairAp300.Driver;

/// <summary>
/// Driver for Altair AP-3000 Installation Projector.
/// Handles sending commands, receiving responses, and maintaining projector state.
/// </summary>
public class AltairDriver : IDisposable
{
    private readonly ITcpClient _client;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private TaskCompletionSource<string>? _pendingResponseTcs;
    private readonly object _reconnectLock = new();
    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;
    private bool _manualDisconnectRequested;
    private bool _isDisposed;

    #region Configuration & State Properties

    /// <summary>
    /// Target IP address or hostname.
    /// </summary>
    public string IpAddress { get; }

    /// <summary>
    /// Target TCP port (default 5100).
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Command timeout in seconds.
    /// </summary>
    public int CommandTimeout { get; }

    /// <summary>
    /// Command retry attempts.
    /// </summary>
    public int CommandRetries { get;}

    /// <summary>
    /// Enable automatic reconnection on connection loss (default true).
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// Alias for AutoReconnect property.
    /// </summary>
    public bool AutoReconect
    {
        get => AutoReconnect;
        set => AutoReconnect = value;
    }

    /// <summary>
    /// Power state: true = On, false = Off/Standby.
    /// </summary>
    public bool Power { get; private set; }

    /// <summary>
    /// Current input source (1–4).
    /// </summary>
    public int Source { get; private set; }

    /// <summary>
    /// Current light output percentage (0–100).
    /// </summary>
    public int lightOutput { get; private set; }

    /// <summary>
    /// Alias for lightOutput property.
    /// </summary>
    public int LightOutput => lightOutput;

    /// <summary>
    /// Shutter state: true = Closed (blank), false = Open.
    /// </summary>
    public bool Shutter { get; private set; }
    
    public string FW{ get; private set; }

    /// <summary>
    /// Indicates connection status to the device.
    /// </summary>
    public bool IsConnected => _client.IsConnected;

    /// <summary>
    /// Firmware version of the device protocol.
    /// </summary>
    public string FirmwareVersion { get; private set; } = "1.07";

    #endregion

    #region Events

    /// <summary>
    /// Event raised when Power state changes.
    /// </summary>
    public event Action<bool>? PowerStateChanged;

    /// <summary>
    /// Event raised when Source state changes.
    /// </summary>
    public event Action<int>? SourceStateChanged;

    /// <summary>
    /// Event raised when Shutter state changes.
    /// </summary>
    public event Action<bool>? ShutterStateChanged;

    /// <summary>
    /// Event raised when lightOutput state changes.
    /// </summary>
    public event Action<int>? lightOutputStateChanged;

    /// <summary>
    /// Event raised when connected to device.
    /// </summary>
    public event Action? Connected;

    /// <summary>
    /// Event raised when disconnected from device.
    /// </summary>
    public event Action? Disconnected;

    #endregion

    /// <summary>
    /// Initializes driver with target ipAddress, port, command timeout (seconds), retries, autoReconnect, and optional ITcpClient transport.
    /// </summary>
    public AltairDriver(
        string ipAddress = "localhost",
        int port = 5100,
        int commandTimeout = 2,
        int commandRetries = 3,
        bool autoReconnect = true,
        ITcpClient? client = null)
    {
        _client = client ?? new AltairTcpClient();
        IpAddress = ipAddress ?? "localhost";
        Port = port;
        CommandTimeout = commandTimeout;
        CommandRetries = commandRetries;
        AutoReconnect = autoReconnect;

        _client.DataReceived += OnDataReceived;
        _client.Connected += OnConnected;
        _client.Disconnected += OnDisconnected;
    }

    #region Connection Management

    public async Task ConnectAsync(string? host = null, int? port = null, CancellationToken cancellationToken = default)
    {
        _manualDisconnectRequested = false;
        string targetHost = !string.IsNullOrWhiteSpace(host) ? host : IpAddress;
        int targetPort = port ?? Port;
        await _client.ConnectAsync(targetHost, targetPort, cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        _manualDisconnectRequested = true;
        _reconnectCts?.Cancel();
        await _client.DisconnectAsync();
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        Connected?.Invoke();
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        Disconnected?.Invoke();

        if (AutoReconnect && !_manualDisconnectRequested && !_isDisposed)
        {
            StartAutoReconnect();
        }
    }

    private void StartAutoReconnect()
    {
        lock (_reconnectLock)
        {
            if (_reconnectTask != null && !_reconnectTask.IsCompleted) return;

            _reconnectCts?.Cancel();
            _reconnectCts = new CancellationTokenSource();
            var token = _reconnectCts.Token;

            _reconnectTask = Task.Run(() => AutoReconnectLoopAsync(token), token);
        }
    }

    private async Task AutoReconnectLoopAsync(CancellationToken cancellationToken)
    {
        int attempt = 1;
        int delaySeconds = 1;

        while (AutoReconnect && !_manualDisconnectRequested && !IsConnected && !cancellationToken.IsCancellationRequested && !_isDisposed)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);

                if (cancellationToken.IsCancellationRequested || _manualDisconnectRequested) break;

                await ConnectAsync(cancellationToken: cancellationToken);

                if (IsConnected)
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                attempt++;
                delaySeconds = Math.Min(60, delaySeconds + attempt);
                Console.WriteLine($"[AUTO-RECONNECT] Attempt #{attempt - 1} failed ({ex.Message}). Retrying in {delaySeconds}s...");
            }
        }
    }

    #endregion

    #region State Control Methods

    /// <summary>
    /// Powers the projector on (true) or off/standby (false).
    /// </summary>
    public async Task SetPowerAsync(bool on, CancellationToken cancellationToken = default)
    {
        string cmd = on ? "SYS:1;" : "SYS:0;";
        string response = await SendCommandAsync(cmd, cancellationToken);
        if (response == "ACK" || response == (on ? "SYS:1" : "SYS:0"))
        {
            UpdatePower(on);
        }
    }

    /// <summary>
    /// Selects input source (1–4).
    /// </summary>
    public async Task SetSourceAsync(int source, CancellationToken cancellationToken = default)
    {
        if (source < 1 || source > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(source), "Source must be between 1 and 4.");
        }

        string cmd = $"SRC:{source};";
        string response = await SendCommandAsync(cmd, cancellationToken);
        if (response == "ACK" || response == $"SRC:{source}")
        {
            UpdateSource(source);
        }
    }

    /// <summary>
    /// Sets light output percentage (0–100).
    /// </summary>
    public async Task SetLightOutputAsync(int value, CancellationToken cancellationToken = default)
    {
        if (value < 0 || value > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Light output must be between 0 and 100.");
        }

        string cmd = $"LGT:{value};";
        string response = await SendCommandAsync(cmd, cancellationToken);
        if (response == "ACK" || response == $"LGT:{value}")
        {
            UpdatelightOutput(value);
        }
    }

    /// <summary>
    /// Opens (false) or closes (true) the shutter.
    /// </summary>
    public async Task SetShutterAsync(bool closed, CancellationToken cancellationToken = default)
    {
        string cmd = closed ? "SHT:1;" : "SHT:0;";
        string response = await SendCommandAsync(cmd, cancellationToken);
        if (response == "ACK" || response == (closed ? "SHT:1" : "SHT:0"))
        {
            UpdateShutter(closed);
        }
    }

    #endregion

    #region State Query Methods

    /// <summary>
    /// Queries the current power state from the projector.
    /// </summary>
    public async Task<bool> QueryPowerAsync(CancellationToken cancellationToken = default)
    {
        string response = await SendCommandAsync("SYS:?;", cancellationToken);
        if (response == "SYS:1")
        {
            UpdatePower(true);
            return true;
        }
        if (response == "SYS:0")
        {
            UpdatePower(false);
            return false;
        }
        return Power;
    }

    /// <summary>
    /// Queries the selected input source from the projector.
    /// </summary>
    public async Task<int> QuerySourceAsync(CancellationToken cancellationToken = default)
    {
        string response = await SendCommandAsync("SRC:?;", cancellationToken);
        if (response.StartsWith("SRC:") && int.TryParse(response.AsSpan(4), out int src))
        {
            UpdateSource(src);
            return src;
        }
        return Source;
    }

    /// <summary>
    /// Queries the light output level from the projector.
    /// </summary>
    public async Task<int> QueryLightOutputAsync(CancellationToken cancellationToken = default)
    {
        string response = await SendCommandAsync("LGT:?;", cancellationToken);
        if (response.StartsWith("LGT:") && int.TryParse(response.AsSpan(4), out int lgt))
        {
            UpdatelightOutput(lgt);
            return lgt;
        }
        return lightOutput;
    }

    /// <summary>
    /// Queries the shutter state from the projector.
    /// </summary>
    public async Task<bool> QueryShutterAsync(CancellationToken cancellationToken = default)
    {
        string response = await SendCommandAsync("SHT:?;", cancellationToken);
        if (response == "SHT:1")
        {
            UpdateShutter(true);
            return true;
        }
        if (response == "SHT:0")
        {
            UpdateShutter(false);
            return false;
        }
        return Shutter;
    }

    #endregion

    #region Protocol Messaging & State Internal Handlers

    private async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            int cycleAttempt = 0;
            int reconnectDelaySeconds = 2; // Initial reconnect wait delay: 2 seconds

            while (!cancellationToken.IsCancellationRequested && !_isDisposed)
            {
                if (!IsConnected)
                {
                    await ConnectAsync(cancellationToken: cancellationToken);
                }

                int retries = Math.Max(1, CommandRetries);
                int timeoutSeconds = Math.Max(1, CommandTimeout);
                bool feedbackReceived = false;

                for (int attempt = 1; attempt <= retries; attempt++)
                {
                    try
                    {
                        _pendingResponseTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                        using (linkedCts.Token.Register(() => _pendingResponseTcs.TrySetCanceled(linkedCts.Token)))
                        {
                            Console.WriteLine($"TX: {command.Trim()}");
                            await _client.SendAsync(command, cancellationToken);
                            string response = await _pendingResponseTcs.Task;

                            feedbackReceived = true;

                            if (response.StartsWith("NAK:"))
                            {
                                throw new InvalidOperationException($"Projector returned error response: {response}");
                            }

                            return response;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Protocol NAK error received from device -> feedback was received, rethrow exception
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        Console.WriteLine($"[NO FEEDBACK] Attempt {attempt}/{retries} for '{command.Trim()}' failed: {ex.Message}");
                    }
                    finally
                    {
                        _pendingResponseTcs = null;
                    }
                }

                // If after CommandRetries there was still no feedback, perform recovery cycle
                if (!feedbackReceived)
                {
                    Console.WriteLine($"[RECOVERY] No feedback received after {retries} attempt(s) for '{command.Trim()}'. Disconnecting...");

                    _manualDisconnectRequested = true;
                    await _client.DisconnectAsync();
                    _manualDisconnectRequested = false;

                    Console.WriteLine($"[RECOVERY] Waiting {reconnectDelaySeconds}s before reconnecting...");
                    await Task.Delay(TimeSpan.FromSeconds(reconnectDelaySeconds), cancellationToken);

                    // Accumulate reconnect delay for next potential cycle (capped at 60s)
                    cycleAttempt++;
                    reconnectDelaySeconds = Math.Min(60, reconnectDelaySeconds + cycleAttempt + 1);

                    Console.WriteLine($"[RECOVERY] Reconnecting to target device...");
                    await ConnectAsync(cancellationToken: cancellationToken);
                    // Note: ConnectAsync includes 1s pause after connection
                }
            }

            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private void OnDataReceived(object? sender, string message)
    {
        string trimmed = message.Trim();
        Console.WriteLine($"RX: {trimmed}");

        if (trimmed.StartsWith("SYS:"))
        {
            if (int.TryParse(trimmed.AsSpan(4), out int pwr))
            {
                UpdatePower(pwr == 1);
            }
        }
        else if (trimmed.StartsWith("SRC:"))
        {
            if (int.TryParse(trimmed.AsSpan(4), out int src))
            {
                UpdateSource(src);
            }
        }
        else if (trimmed.StartsWith("LGT:"))
        {
            if (int.TryParse(trimmed.AsSpan(4), out int lgt))
            {
                UpdatelightOutput(lgt);
            }
        }
        else if (trimmed.StartsWith("SHT:"))
        {
            if (int.TryParse(trimmed.AsSpan(4), out int sht))
            {
                UpdateShutter(sht == 1);
            }
        }
        //     !ID:AP-3000:1.07
        else if (trimmed.StartsWith("!ID:"))
        {
            var fw = trimmed.AsSpan(12).ToString();
            UpdateFw(fw);
        }

        _pendingResponseTcs?.TrySetResult(trimmed);
    }

    private void UpdatePower(bool newPower)
    {
        if (Power != newPower)
        {
            Power = newPower;
            PowerStateChanged?.Invoke(Power);
        }
    }

    private void UpdateSource(int newSource)
    {
        if (Source != newSource)
        {
            Source = newSource;
            SourceStateChanged?.Invoke(Source);
        }
    }

    private void UpdatelightOutput(int newLightOutput)
    {
        if (lightOutput != newLightOutput)
        {
            lightOutput = newLightOutput;
            lightOutputStateChanged?.Invoke(lightOutput);
        }
    }

    private void UpdateShutter(bool newShutter)
    {
        if (Shutter != newShutter)
        {
            Shutter = newShutter;
            ShutterStateChanged?.Invoke(Shutter);
        }
    }
    
    private void UpdateFw(string newFw)
    {
        if (FW != newFw)
        {
            FW = newFw;
        }
    }

    #endregion

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _client.DataReceived -= OnDataReceived;
        _client.Connected -= OnConnected;
        _client.Disconnected -= OnDisconnected;
        _client.Dispose();
        _commandLock.Dispose();
        GC.SuppressFinalize(this);
    }
}