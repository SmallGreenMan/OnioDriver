using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AltairAp300.Driver;

namespace AltairAp300.Driver.Tests;

public class FakeTcpClient : ITcpClient
{
    private readonly List<string> _sentCommands = new();
    private readonly object _lock = new();

    public bool IsConnected { get; private set; }
    public IReadOnlyList<string> SentCommands
    {
        get
        {
            lock (_lock)
            {
                return _sentCommands.ToArray();
            }
        }
    }

    public bool AutoGreeting { get; set; } = true;
    public bool SimulateDenyOnConnect { get; set; }

    public event EventHandler<string>? DataReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    // Response auto-reply configuration for testing
    public Func<string, string>? AutoResponseHandler { get; set; }

    public Task ConnectAsync(string host, int port = 5100, CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        Connected?.Invoke(this, EventArgs.Empty);

        if (SimulateDenyOnConnect)
        {
            Task.Run(() => DataReceived?.Invoke(this, "!DENY"));
        }
        else if (AutoGreeting)
        {
            Task.Run(() => DataReceived?.Invoke(this, "!ID:AP-3000:1.07"));
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task SendAsync(string data, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _sentCommands.Add(data);
        }

        if (AutoResponseHandler != null)
        {
            string reply = AutoResponseHandler(data);
            if (!string.IsNullOrEmpty(reply))
            {
                // Fire data received asynchronously
                Task.Run(() => DataReceived?.Invoke(this, reply), cancellationToken);
            }
        }
        return Task.CompletedTask;
    }

    public void SimulateIncomingData(string data)
    {
        DataReceived?.Invoke(this, data);
    }

    public void SimulateRemoteDisconnect()
    {
        IsConnected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        IsConnected = false;
    }
}
