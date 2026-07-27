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
    private bool _isDisposed;

    #region Configuration & State Properties

    /// <summary>
    /// Target IP address or hostname.
    /// </summary>
    public string IpAddress { get; set; }

    /// <summary>
    /// Target TCP port (default 5100).
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Command timeout in seconds.
    /// </summary>
    public int CommandTimeout { get; set; }

    /// <summary>
    /// Command retry attempts.
    /// </summary>
    public int CommandRetries { get; set; }

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
    public event Action? Disconected;

    /// <summary>
    /// Event raised when disconnected from device (standard spelling alias).
    /// </summary>
    public event Action? Disconnected;

    #endregion

    /// <summary>
    /// Initializes driver with target ipAddress, port, command timeout (seconds), retries, and optional ITcpClient transport.
    /// </summary>
    public AltairDriver(
        string ipAddress = "localhost",
        int port = 5100,
        int commandTimeout = 5,
        int commandRetries = 5,
        ITcpClient? client = null)
    {
        _client = client ?? new AltairTcpClient();
        IpAddress = ipAddress ?? "localhost";
        Port = port;
        CommandTimeout = commandTimeout;
        CommandRetries = commandRetries;

        _client.DataReceived += OnDataReceived;
        _client.Connected += OnConnected;
        _client.Disconnected += OnDisconnected;
    }

    #region Connection Management

    public async Task ConnectAsync(string? host = null, int? port = null, CancellationToken cancellationToken = default)
    {
        string targetHost = !string.IsNullOrWhiteSpace(host) ? host : IpAddress;
        int targetPort = port ?? Port;
        await _client.ConnectAsync(targetHost, targetPort, cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        await _client.DisconnectAsync();
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        Connected?.Invoke();
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        Disconected?.Invoke();
        Disconnected?.Invoke();
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

    public Task PowerAsync(bool on, CancellationToken cancellationToken = default) => SetPowerAsync(on, cancellationToken);

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

    public Task SourceAsync(int source, CancellationToken cancellationToken = default) => SetSourceAsync(source, cancellationToken);

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

    public Task LightOutputAsync(int value, CancellationToken cancellationToken = default) => SetLightOutputAsync(value, cancellationToken);

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

    public Task ShutterAsync(bool closed, CancellationToken cancellationToken = default) => SetShutterAsync(closed, cancellationToken);

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

    public Task<bool> GetPowerAsync(CancellationToken cancellationToken = default) => QueryPowerAsync(cancellationToken);

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

    public Task<int> GetSourceAsync(CancellationToken cancellationToken = default) => QuerySourceAsync(cancellationToken);

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

    public Task<int> GetLightOutputAsync(CancellationToken cancellationToken = default) => QueryLightOutputAsync(cancellationToken);

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

    public Task<bool> GetShutterAsync(CancellationToken cancellationToken = default) => QueryShutterAsync(cancellationToken);

    #endregion

    #region Protocol Messaging & State Internal Handlers

    private async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsConnected)
            {
                await ConnectAsync(cancellationToken: cancellationToken);
            }

            int retries = Math.Max(1, CommandRetries);
            int timeoutSeconds = Math.Max(1, CommandTimeout);
            Exception? lastException = null;

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

                        if (response.StartsWith("NAK:"))
                        {
                            throw new InvalidOperationException($"Projector returned error response: {response}");
                        }

                        return response;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Immediate rethrow for protocol NAK errors or explicit invalid operation errors
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                }
                finally
                {
                    _pendingResponseTcs = null;
                }
            }

            throw new TimeoutException($"Command '{command}' timed out after {retries} attempt(s).", lastException);
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