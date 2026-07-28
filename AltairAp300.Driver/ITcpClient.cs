using System;
using System.Threading;
using System.Threading.Tasks;

namespace AltairAp300.Driver;

/// Abstraction for TCP network transport to allow unit testing of AltairDriver without real TCP socket connections.
public interface ITcpClient : IDisposable
{
    /// Indicates whether the transport client is currently connected.
    bool IsConnected { get; }

    /// Connects asynchronously to the target host and port.
    Task ConnectAsync(string host, int port = 5100, CancellationToken cancellationToken = default);

    /// Disconnects asynchronously from the target host.
    Task DisconnectAsync();

    /// Sends data string asynchronously over the socket.
    Task SendAsync(string data, CancellationToken cancellationToken = default);

    /// Event raised when data is received from the device.
    event EventHandler<string>? DataReceived;

    /// Event raised when the transport is connected.
    event EventHandler? Connected;

    /// Event raised when the transport is disconnected.
    event EventHandler? Disconnected;
}
