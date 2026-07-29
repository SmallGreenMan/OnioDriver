using System;
using System.Threading;
using System.Threading.Tasks;

namespace AltairAp300.Driver;

/// Driver for Altair AP-3000 Installation Projector. Handles sending commands, receiving responses, and maintaining projector state.
public class AltairDriver : IDisposable
{
    private readonly ITcpClient _client;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private TaskCompletionSource<string>? _pendingResponseTcs;
    private readonly object _reconnectLock = new();
    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;
    private readonly object _stateLock = new();
    private TaskCompletionSource<bool>? _stateTransitionTcs;
    private CancellationTokenSource? _powerPollingCts;
    private Task? _powerPollingTask;
    private TaskCompletionSource<bool>? _connectionGreetingTcs;
    private TaskCompletionSource<bool>? _initialQueryTcs;
    private bool _manualDisconnectRequested;
    private bool _isDisposed;

    #region Configuration & State Properties

    /// Target IP address or hostname.
    public string IpAddress { get; }

    /// Target TCP port (default 5100).
    public int Port { get; }

    /// Command timeout in seconds.
    public int CommandTimeout { get; }

    /// Command retry attempts.
    public int CommandRetries { get; }

    /// Enable automatic reconnection on connection loss (default true).
    public bool AutoReconnect { get; set; } = true;

    /// Alias for AutoReconnect property.
    public bool AutoReconect
    {
        get => AutoReconnect;
        set => AutoReconnect = value;
    }

    /// Enable debug console output for data exchange with device (default false).
    public bool Debug { get; set; }

    #endregion

    #region Configurable Timing & Retry Properties

    /// Initial delay in seconds for auto-reconnect attempts (default: 1).
    public int AutoReconnectInitialDelaySeconds { get; set; } = 1;

    /// Maximum delay in seconds for auto-reconnect attempts (default: 60).
    public int AutoReconnectMaxDelaySeconds { get; set; } = 60;

    /// Initial reconnect delay in seconds when command recovery is triggered (default: 2).
    public int InitialRecoveryReconnectDelaySeconds { get; set; } = 2;

    /// Maximum reconnect delay in seconds when command recovery is triggered (default: 60).
    public int MaxRecoveryReconnectDelaySeconds { get; set; } = 60;

    /// Polling interval in seconds for power state during intermediate transitions (default: 3).
    public int PowerPollingIntervalSeconds { get; set; } = 3;

    #endregion

    #region State Properties

    /// Power state: Unknown (null), Off (0), On (1), SwitchingOn (2), SwitchingOff (3).
    public PowerState? Power { get; private set; }

    /// Current input source (1–4) or null if unknown.
    public int? Source { get; private set; }

    /// Current light output percentage (0–100) or null if unknown.
    public int? lightOutput { get; private set; }

    /// Alias for lightOutput property.
    public int? LightOutput => lightOutput;

    /// Shutter state: true = Closed (blank), false = Open, null = Unknown.
    public bool? Shutter { get; private set; }
    
    public string FW { get; private set; } = string.Empty;

    /// Indicates fiscal connection status to the device.
    public bool IsConnected => _client.IsConnected;
    
    /// Indicates whether initial device state polling has completed and the driver is ready (Logical connection status).
    public bool DeviceIsReady { get; private set; }
    
    /// Firmware version of the device protocol.
    public string FirmwareVersion { get; private set; } = "1.07";


    #endregion

    #region Events

    /// Event raised when Power state changes.
    public event Action<PowerState?>? PowerStateChanged;

    /// Event raised when device receives !RDY (System On).
    public event Action? Ready;

    /// Event raised when device receives !STBY (System Off).
    public event Action? Standby;

    /// Event raised when Source state changes.
    public event Action<int?>? SourceStateChanged;

    /// Event raised when Shutter state changes.
    public event Action<bool?>? ShutterStateChanged;

    /// Event raised when lightOutput state changes.
    public event Action<int?>? lightOutputStateChanged;

    /// Event raised when connected to device.
    public event Action? Connected;

    /// Event raised when disconnected from device.
    public event Action? Disconected;

    /// Event raised when disconnected from device (standard spelling alias).
    public event Action? Disconnected;

    /// Event raised when initial state query completes (true) or when device disconnects (false).
    public event Action<bool>? DeviceIsReadyChanged;

    #endregion

    /// Initializes driver with target ipAddress, port, command timeout (seconds), retries, autoReconnect, debug flag, and optional ITcpClient transport.
    public AltairDriver(
        string ipAddress = "localhost",
        int port = 5100,
        int commandTimeout = 2,
        int commandRetries = 3,
        bool autoReconnect = true,
        bool debug = false,
        ITcpClient? client = null)
    {
        _client = client ?? new AltairTcpClient();
        IpAddress = ipAddress ?? "localhost";
        Port = port;
        CommandTimeout = commandTimeout;
        CommandRetries = commandRetries;
        AutoReconnect = autoReconnect;
        Debug = debug;

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

        int cycleAttempt = 0;
        int reconnectDelaySeconds = Math.Max(1, InitialRecoveryReconnectDelaySeconds);

        while (!cancellationToken.IsCancellationRequested && !_manualDisconnectRequested && !_isDisposed)
        {
            _connectionGreetingTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                await _client.ConnectAsync(targetHost, targetPort, cancellationToken);

                using var greetingCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(3, CommandTimeout)));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, greetingCts.Token);

                using (linkedCts.Token.Register(() => _connectionGreetingTcs.TrySetCanceled(linkedCts.Token)))
                {
                    await _connectionGreetingTcs.Task;
                    return;
                }
            }
            catch (Exception ex)
            {
                _manualDisconnectRequested = true;
                await _client.DisconnectAsync();
                _manualDisconnectRequested = false;

                if (!AutoReconnect || cancellationToken.IsCancellationRequested || _manualDisconnectRequested || _isDisposed)
                {
                    throw;
                }

                if (Debug) Console.WriteLine($"[RECOVERY] Connection failed or denied ({ex.Message}). Retrying in {reconnectDelaySeconds}s...");
                await Task.Delay(TimeSpan.FromSeconds(reconnectDelaySeconds), cancellationToken);

                cycleAttempt++;
                int maxDelay = Math.Max(1, MaxRecoveryReconnectDelaySeconds);
                reconnectDelaySeconds = Math.Min(maxDelay, reconnectDelaySeconds + cycleAttempt + 1);
            }
        }
    }

    public async Task DisconnectAsync()
    {
        _manualDisconnectRequested = true;
        _reconnectCts?.Cancel();
        UpdateDeviceIsReady(false);
        await _client.DisconnectAsync();
    }

    public void Disconnect()
    {
        _manualDisconnectRequested = true;
        _reconnectCts?.Cancel();
        UpdateDeviceIsReady(false);
        _client.DisconnectAsync().GetAwaiter().GetResult();
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        Connected?.Invoke();
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        UpdateDeviceIsReady(false);
        Disconected?.Invoke();
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
        int delaySeconds = Math.Max(1, AutoReconnectInitialDelaySeconds);

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
                int maxDelay = Math.Max(1, AutoReconnectMaxDelaySeconds);
                delaySeconds = Math.Min(maxDelay, delaySeconds + attempt);
                if (Debug) Console.WriteLine($"[AUTO-RECONNECT] Attempt #{attempt - 1} failed ({ex.Message}). Retrying in {delaySeconds}s...");
            }
        }
    }

    #endregion

    #region State Control Methods

    /// Powers the projector on (true) or off/standby (false).
    public async Task SetPowerAsync(bool on, CancellationToken cancellationToken = default)
    {
        try
        {
            string cmd = on ? "SYS:1;" : "SYS:0;";
            string response = await SendCommandAsync(cmd, cancellationToken);
            if (response == "ACK")
            {
                UpdatePowerState(on ? PowerState.SwitchingOn : PowerState.SwitchingOff);
                _ = QueryPowerAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            if (Debug) Console.WriteLine($"[ERROR] SetPowerAsync failed: {ex.Message}");
        }
    }

    /// Powers the projector on asynchronously.
    public Task PowerOnAsync(CancellationToken cancellationToken = default) => SetPowerAsync(true, cancellationToken);

    /// Powers the projector off (standby) asynchronously.
    public Task PowerOffAsync(CancellationToken cancellationToken = default) => SetPowerAsync(false, cancellationToken);
    
    /// Selects input source (1–4).
    public async Task SetSourceAsync(int source, CancellationToken cancellationToken = default)
    {
        if (source < 1 || source > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(source), "Source must be between 1 and 4.");
        }

        try
        {
            string cmd = $"SRC:{source};";
            string response = await SendCommandAsync(cmd, cancellationToken);
            if (response == "ACK")
            {
                await QuerySourceAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            if (Debug) Console.WriteLine($"[ERROR] SetSourceAsync failed: {ex.Message}");
        }
    }

    /// Sets light output percentage (0–100).
    public async Task SetLightOutputAsync(int value, CancellationToken cancellationToken = default)
    {
        if (value < 0 || value > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Light output must be between 0 and 100.");
        }

        try
        {
            string cmd = $"LGT:{value};";
            string response = await SendCommandAsync(cmd, cancellationToken);
            if (response == "ACK")
            {
                await QueryLightOutputAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            if (Debug) Console.WriteLine($"[ERROR] SetLightOutputAsync failed: {ex.Message}");
        }
    }

    /// Opens (false) or closes (true) the shutter.
    public async Task SetShutterAsync(bool closed, CancellationToken cancellationToken = default)
    {
        try
        {
            string cmd = closed ? "SHT:1;" : "SHT:0;";
            string response = await SendCommandAsync(cmd, cancellationToken);
            if (response == "ACK")
            {
                await QueryShutterAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            if (Debug) Console.WriteLine($"[ERROR] SetShutterAsync failed: {ex.Message}");
        }
    }

    #endregion

    #region State Query Methods

    /// Queries the current power state from the projector. Returns 0 (Off), 1 (On), 2 (SwitchingOn), 3 (SwitchingOff), or null if unknown.
    public async Task<PowerState?> QueryPowerAsync(CancellationToken cancellationToken = default, bool isInitialQuery = false)
    {
        try
        {
            string response = await SendCommandAsync("SYS:?;", cancellationToken, isInitialQuery);
            if (response.StartsWith("SYS:") && int.TryParse(response.AsSpan(4), out int pwrCode))
            {
                if (Enum.IsDefined(typeof(PowerState), pwrCode))
                {
                    var state = (PowerState)pwrCode;
                    UpdatePowerState(state);
                    return state;
                }
            }
        }
        catch (Exception ex)
        {
            if (Debug) Console.WriteLine($"[ERROR] QueryPowerAsync failed: {ex.Message}");
        }
        return Power;
    }

    /// Queries the selected input source from the projector.
    public async Task<int?> QuerySourceAsync(CancellationToken cancellationToken = default, bool isInitialQuery = false)
    {
        try
        {
            string response = await SendCommandAsync("SRC:?;", cancellationToken, isInitialQuery);
            if (response.StartsWith("SRC:") && int.TryParse(response.AsSpan(4), out int src))
            {
                UpdateSource(src);
                return src;
            }
        }
        catch (Exception ex)
        {
            if (Debug) Console.WriteLine($"[ERROR] QuerySourceAsync failed: {ex.Message}");
        }
        return Source;
    }

    /// Queries the light output level from the projector. Converts raw feedback (0–255) to percentage (0–100).
    public async Task<int?> QueryLightOutputAsync(CancellationToken cancellationToken = default, bool isInitialQuery = false)
    {
        try
        {
            string response = await SendCommandAsync("LGT:?;", cancellationToken, isInitialQuery);
            if (response.StartsWith("LGT:") && int.TryParse(response.AsSpan(4), out int rawLgt))
            {
                int scaledLgt = ConvertRawLightOutputToPercent(rawLgt);
                UpdatelightOutput(scaledLgt);
                return scaledLgt;
            }
        }
        catch (Exception ex)
        {
            if (Debug) Console.WriteLine($"[ERROR] QueryLightOutputAsync failed: {ex.Message}");
        }
        return lightOutput;
    }

    /// Queries the shutter state from the projector.
    public async Task<bool?> QueryShutterAsync(CancellationToken cancellationToken = default, bool isInitialQuery = false)
    {
        try
        {
            string response = await SendCommandAsync("SHT:?;", cancellationToken, isInitialQuery);
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
        }
        catch (Exception ex)
        {
            if (Debug) Console.WriteLine($"[ERROR] QueryShutterAsync failed: {ex.Message}");
        }
        return Shutter;
    }

    #endregion

    #region Protocol Messaging & State Internal Handlers

    private async Task EnsureInitialQueryCompletedAsync(CancellationToken cancellationToken)
    {
        Task? waitTask;
        lock (_stateLock)
        {
            waitTask = _initialQueryTcs?.Task;
        }

        if (waitTask != null && !waitTask.IsCompleted)
        {
            if (Debug) Console.WriteLine("[QUEUE] Waiting for initial device state query to complete...");
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await Task.WhenAny(waitTask, Task.Delay(Timeout.Infinite, linkedCts.Token));
        }
    }

    private async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken = default, bool isInitialQuery = false)
    {
        if (!isInitialQuery)
        {
            await EnsureInitialQueryCompletedAsync(cancellationToken);
        }

        bool isPowerCommand = command.StartsWith("SYS:", StringComparison.OrdinalIgnoreCase);

        if (!isPowerCommand)
        {
            await EnsureOperationalStateAsync(cancellationToken);
        }

        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            int cycleAttempt = 0;
            int reconnectDelaySeconds = Math.Max(0, InitialRecoveryReconnectDelaySeconds);

            while (!cancellationToken.IsCancellationRequested && !_isDisposed)
            {
                if (!IsConnected)
                {
                    throw new AltairNotConnectedException();
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
                            if (Debug) Console.WriteLine($"TX: {command.Trim()}");
                            await _client.SendAsync(command, cancellationToken);
                            string response = await _pendingResponseTcs.Task;

                            feedbackReceived = true;

                            if (response.StartsWith("NAK:", StringComparison.OrdinalIgnoreCase))
                            {
                                throw AltairNakException.FromResponse(response);
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
                        if (Debug) Console.WriteLine($"[NO FEEDBACK] Attempt {attempt}/{retries} for '{command.Trim()}' failed: {ex.Message}");
                    }
                    finally
                    {
                        _pendingResponseTcs = null;
                    }
                }

                // If after CommandRetries there was still no feedback, perform recovery cycle
                if (!feedbackReceived)
                {
                    if (Debug) Console.WriteLine($"[RECOVERY] No feedback received after {retries} attempt(s) for '{command.Trim()}'. Disconnecting...");

                    _manualDisconnectRequested = true;
                    await _client.DisconnectAsync();
                    _manualDisconnectRequested = false;

                    if (Debug) Console.WriteLine($"[RECOVERY] Waiting {reconnectDelaySeconds}s before reconnecting...");
                    await Task.Delay(TimeSpan.FromSeconds(reconnectDelaySeconds), cancellationToken);

                    // Accumulate reconnect delay for next potential cycle (capped at MaxRecoveryReconnectDelaySeconds)
                    cycleAttempt++;
                    int maxRecoveryDelay = Math.Max(1, MaxRecoveryReconnectDelaySeconds);
                    reconnectDelaySeconds = Math.Min(maxRecoveryDelay, reconnectDelaySeconds + cycleAttempt + 1);

                    if (Debug) Console.WriteLine($"[RECOVERY] Reconnecting to target device...");
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
        if (Debug) Console.WriteLine($"RX: {trimmed}");

        bool isUnsolicited = false;

        if (trimmed.Equals("!RDY", StringComparison.OrdinalIgnoreCase))
        {
            isUnsolicited = true;
            UpdatePowerState(PowerState.On);
            Ready?.Invoke();
        }
        else if (trimmed.Equals("!STBY", StringComparison.OrdinalIgnoreCase))
        {
            isUnsolicited = true;
            UpdatePowerState(PowerState.Off);
            Standby?.Invoke();
        }
        else if (trimmed.Equals("!DENY", StringComparison.OrdinalIgnoreCase))
        {
            isUnsolicited = true;
            if (Debug) Console.WriteLine("[CONNECT] Connection denied: Another client is connected.");
            _connectionGreetingTcs?.TrySetException(new InvalidOperationException("[CONNECT] Connection denied: Another client is Connected"));
        }
        else if (trimmed.StartsWith("!ID:", StringComparison.OrdinalIgnoreCase))
        {
            isUnsolicited = true;
            string[] parts = trimmed.Split(':');
            string fwVersion = parts.Length >= 3 ? parts[2] : (trimmed.Length >= 12 ? trimmed.Substring(12) : trimmed);
            UpdateFw(fwVersion);

            Connected?.Invoke();
            _connectionGreetingTcs?.TrySetResult(true);

            _ = Task.Run(async () =>
            {
                try
                {
                    await QueryAllStatesAsync(cancellationToken: CancellationToken.None, isInitialQuery: true);
                }
                catch (Exception ex)
                {
                    if (Debug) Console.WriteLine($"[INIT] Failed to query device states: {ex.Message}");
                }
            });
        }
        else if (trimmed.StartsWith("SYS:"))
        {
            if (int.TryParse(trimmed.AsSpan(4), out int pwrCode))
            {
                if (Enum.IsDefined(typeof(PowerState), pwrCode))
                {
                    UpdatePowerState((PowerState)pwrCode);
                }
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
            if (int.TryParse(trimmed.AsSpan(4), out int rawLgt))
            {
                int scaledLgt = ConvertRawLightOutputToPercent(rawLgt);
                UpdatelightOutput(scaledLgt);
            }
        }
        else if (trimmed.StartsWith("SHT:"))
        {
            if (int.TryParse(trimmed.AsSpan(4), out int sht))
            {
                UpdateShutter(sht == 1);
            }
        }

        if (!isUnsolicited)
        {
            _pendingResponseTcs?.TrySetResult(trimmed);
        }
    }

    private static int ConvertRawLightOutputToPercent(int rawValue)
    {
        return (int)Math.Clamp(Math.Round(rawValue * 100.0 / 255.0), 0, 100);
    }

    private void UpdatePowerState(PowerState? newPowerState)
    {
        if (Power != newPowerState)
        {
            Power = newPowerState;
            PowerStateChanged?.Invoke(Power);

            if (Power != PowerState.On && Power != PowerState.Off)
            {
                if (Power.HasValue)
                {
                    EnsurePowerPollingRunning();
                }
            }
            else
            {
                StopPowerPolling();

                lock (_stateLock)
                {
                    _stateTransitionTcs?.TrySetResult(true);
                    _stateTransitionTcs = null;
                }
            }
        }
    }

    private async Task EnsureOperationalStateAsync(CancellationToken cancellationToken)
    {
        while (Power != PowerState.On && Power != PowerState.Off)
        {
            Task waitTask;
            lock (_stateLock)
            {
                _stateTransitionTcs ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                waitTask = _stateTransitionTcs.Task;
            }

            EnsurePowerPollingRunning();

            int pollInterval = Math.Max(1, PowerPollingIntervalSeconds);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(pollInterval), linkedCts.Token));
        }
    }

    private void EnsurePowerPollingRunning()
    {
        lock (_stateLock)
        {
            if (_powerPollingTask != null && !_powerPollingTask.IsCompleted) return;

            _powerPollingCts?.Cancel();
            _powerPollingCts = new CancellationTokenSource();
            var token = _powerPollingCts.Token;

            _powerPollingTask = Task.Run(() => PowerPollingLoopAsync(token), token);
        }
    }

    private void StopPowerPolling()
    {
        lock (_stateLock)
        {
            _powerPollingCts?.Cancel();
            _powerPollingCts = null;
            _powerPollingTask = null;
        }
    }

    private async Task PowerPollingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !_isDisposed &&
               Power != PowerState.On && Power != PowerState.Off)
        {
            try
            {
                int pollInterval = Math.Max(1, PowerPollingIntervalSeconds);
                await Task.Delay(TimeSpan.FromSeconds(pollInterval), cancellationToken);
                if (cancellationToken.IsCancellationRequested || _isDisposed) break;

                if (IsConnected)
                {
                    if (Debug) Console.WriteLine("[POWER POLLING] Querying power state during transition...");
                    await QueryPowerAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (Debug) Console.WriteLine($"[POWER POLLING] Query failed: {ex.Message}");
            }
        }
    }

    private void UpdateSource(int? newSource)
    {
        if (Source != newSource)
        {
            Source = newSource;
            SourceStateChanged?.Invoke(Source);
        }
    }

    private void UpdatelightOutput(int? newLightOutput)
    {
        if (lightOutput != newLightOutput)
        {
            lightOutput = newLightOutput;
            lightOutputStateChanged?.Invoke(lightOutput);
        }
    }

    private void UpdateShutter(bool? newShutter)
    {
        if (Shutter != newShutter)
        {
            Shutter = newShutter;
            ShutterStateChanged?.Invoke(Shutter);
        }
    }

    private void ResetStates()
    {
        UpdatePowerState(null);
        UpdateSource(null);
        UpdatelightOutput(null);
        UpdateShutter(null);
    }
    
    /// Queries all device states starting with SYS:?;
    public async Task QueryAllStatesAsync(CancellationToken cancellationToken = default, bool isInitialQuery = false)
    {
        if (isInitialQuery)
        {
            lock (_stateLock)
            {
                _initialQueryTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        try
        {
            if (Debug) Console.WriteLine("[INIT] Querying all device states");
            await QueryPowerAsync(cancellationToken, isInitialQuery);
            await QuerySourceAsync(cancellationToken, isInitialQuery);
            await QueryLightOutputAsync(cancellationToken, isInitialQuery);
            await QueryShutterAsync(cancellationToken, isInitialQuery);

            if (isInitialQuery && IsConnected)
            {
                UpdateDeviceIsReady(true);
            }
        }
        finally
        {
            if (isInitialQuery)
            {
                lock (_stateLock)
                {
                    _initialQueryTcs?.TrySetResult(true);
                }
            }
        }
    }

    private void UpdateDeviceIsReady(bool isReady)
    {
        if (DeviceIsReady != isReady)
        {
            DeviceIsReady = isReady;
            if (!isReady)
            {
                ResetStates();
            }
            DeviceIsReadyChanged?.Invoke(DeviceIsReady);
        }
    }

    private void UpdateFw(string newFw)
    {
        if (FW != newFw)
        {
            FW = newFw;
            FirmwareVersion = newFw;
        }
    }

    #endregion

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        StopPowerPolling();
        _reconnectCts?.Cancel();

        _client.DataReceived -= OnDataReceived;
        _client.Connected -= OnConnected;
        _client.Disconnected -= OnDisconnected;
        _client.Dispose();
        _commandLock.Dispose();
        GC.SuppressFinalize(this);
    }
}