using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AltairAp300.Driver;

/// <summary>
/// TCP client implementation using System.Net.Sockets.TcpClient for connecting to AP-3000 device over Ethernet.
/// </summary>
public class AltairTcpClient : ITcpClient
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
    private bool _isDisposed;

    public bool IsConnected => _tcpClient != null && _tcpClient.Connected;

    public event EventHandler<string>? DataReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public async Task ConnectAsync(string host, int port = 5100, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("---> try to connect");
        if (IsConnected) return;

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port, cancellationToken);

        _stream = _tcpClient.GetStream();
        _reader = new StreamReader(_stream, Encoding.ASCII);
        _writer = new StreamWriter(_stream, Encoding.ASCII) { AutoFlush = true };

        _cts = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);

        // Pause 1 second after connecting
        await Task.Delay(1000, cancellationToken);

        Connected?.Invoke(this, EventArgs.Empty);
    }

    public async Task DisconnectAsync()
    {
        if (_tcpClient == null && _stream == null) return;

        try
        {
            _cts?.Cancel();
            _stream?.Close();
            _tcpClient?.Close();
        }
        catch
        {
            // Ignore disconnect cleanup errors
        }
        finally
        {
            _tcpClient?.Dispose();
            _tcpClient = null;
            _stream = null;
            _reader = null;
            _writer = null;

            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        await Task.CompletedTask;
    }

    public async Task SendAsync(string data, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _writer == null)
        {
            throw new InvalidOperationException("TCP client is not connected to any AP-3000 device.");
        }

        await _sendSemaphore.WaitAsync(cancellationToken);
        try
        {
            await _writer.WriteAsync(data.AsMemory(), cancellationToken);
            await _writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _reader != null)
            {
                string? line = await _reader.ReadLineAsync(cancellationToken);
                if (line == null) break;

                line = line.Trim();
                if (!string.IsNullOrEmpty(line))
                {
                    DataReceived?.Invoke(this, line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Connection cancelled normally
        }
        catch
        {
            // Connection error
        }
        finally
        {
            if (IsConnected)
            {
                _ = DisconnectAsync();
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cts?.Cancel();
        _cts?.Dispose();
        _tcpClient?.Dispose();
        _sendSemaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}
