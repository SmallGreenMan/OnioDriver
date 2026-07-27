using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AltairAp300.Driver;

namespace AltairAp300.Driver.Tests;

public class FakeTcpClient : ITcpClient
{
    public bool IsConnected { get; private set; }
    public List<string> SentCommands { get; } = new();

    public event EventHandler<string>? DataReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    // Response auto-reply configuration for testing
    public Func<string, string>? AutoResponseHandler { get; set; }

    public Task ConnectAsync(string host, int port = 5100, CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        Connected?.Invoke(this, EventArgs.Empty);
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
        SentCommands.Add(data);
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

    public void Dispose()
    {
        IsConnected = false;
    }
}
