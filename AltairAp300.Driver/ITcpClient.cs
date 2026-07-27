using System;
using System.Threading;
using System.Threading.Tasks;

namespace AltairAp300.Driver;

/// <summary>
/// Abstraction for TCP network transport to allow unit testing of AltairDriver without real TCP socket connections.
/// </summary>
public interface ITcpClient : IDisposable
{
    /// <summary>
    /// Indicates whether the transport client is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Connects asynchronously to the target host and port.
    /// </summary>
    Task ConnectAsync(string host, int port = 5100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects asynchronously from the target host.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Sends data string asynchronously over the socket.
    /// </summary>
    Task SendAsync(string data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when data is received from the device.
    /// </summary>
    event EventHandler<string>? DataReceived;

    /// <summary>
    /// Event raised when the transport is connected.
    /// </summary>
    event EventHandler? Connected;

    /// <summary>
    /// Event raised when the transport is disconnected.
    /// </summary>
    event EventHandler? Disconnected;
}
