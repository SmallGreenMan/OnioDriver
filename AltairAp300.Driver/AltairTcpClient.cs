using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AltairAp300.Driver;

public class AltairTcpClient : ITcpClient
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
    private bool _isDisposed;
    private volatile bool _isDisconnected;
    private volatile bool _hasConnected;

    public bool IsConnected
    {
        get
        {
            if (_tcpClient == null || !_tcpClient.Connected) return false;
            try
            {
                Socket socket = _tcpClient.Client;
                if (socket == null || !socket.Connected) return false;

                if (socket.Poll(1000, SelectMode.SelectRead) && socket.Available == 0)
                {
                    return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public event EventHandler<string>? DataReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public async Task ConnectAsync(string host, int port = 5100, CancellationToken cancellationToken = default)
    {
        if (IsConnected) return;
        _isDisconnected = false;

        _tcpClient = new TcpClient();
        try
        {
            _tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        }
        catch
        {
            // Ignore if platform does not support specific socket option
        }

        await _tcpClient.ConnectAsync(host, port, cancellationToken);

        _stream = _tcpClient.GetStream();
        _reader = new StreamReader(_stream, Encoding.ASCII);
        _writer = new StreamWriter(_stream, Encoding.ASCII) { AutoFlush = true };

        _hasConnected = true;
        Connected?.Invoke(this, EventArgs.Empty);

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);
    }

    public Task DisconnectAsync()
    {
        if (_isDisconnected) return Task.CompletedTask;
        _isDisconnected = true;

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

            if (_hasConnected)
            {
                _hasConnected = false;
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        return Task.CompletedTask;
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
        }
        catch (Exception)
        {
            _ = DisconnectAsync();
            throw;
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
            if (!cancellationToken.IsCancellationRequested)
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
        _tcpClient?.Dispose();
        _sendSemaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}
