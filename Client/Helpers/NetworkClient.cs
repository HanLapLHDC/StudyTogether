using System.Threading;
using System.Net.Sockets;
using System.Text.Json;
using Client.Models;

namespace Client.Helpers;

public sealed class NetworkClient
{
    private TcpClient? _tcp;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private SynchronizationContext? _uiContext;
    public event Action<Packet>? PacketReceived;
    public event Action<string>? Disconnected;
    public bool Connected => _tcp?.Connected == true;

    public async Task ConnectAsync(string host, int port)
    {
        // ConnectAsync is normally called from the WinForms UI thread.
        // Capture its context so received packets are delivered on that thread.
        _uiContext = SynchronizationContext.Current;
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(host, port);
        NetworkStream stream = _tcp.GetStream();
        _writer = new StreamWriter(stream) { AutoFlush = true };
        _ = Task.Run(() => ReadLoopAsync(stream));
    }

    public async Task SendAsync(Packet packet)
    {
        if (_writer == null) throw new InvalidOperationException("Chưa kết nối Server.");
        string json = JsonSerializer.Serialize(packet);
        await _sendLock.WaitAsync();
        try { await _writer.WriteLineAsync(json); }
        finally { _sendLock.Release(); }
    }

    private async Task ReadLoopAsync(NetworkStream stream)
    {
        try
        {
            using var reader = new StreamReader(stream);
            string? line;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                Packet? packet;
                try
                {
                    packet = JsonSerializer.Deserialize<Packet>(line, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (JsonException ex)
                {
                    // Ignore a malformed line without terminating the TCP receive loop.
                    DispatchDisconnected($"Invalid packet received: {ex.Message}");
                    continue;
                }

                if (packet != null)
                    DispatchPacket(packet);
            }

            DispatchDisconnected("Server closed the connection.");
        }
        catch (Exception ex) when (
            ex is IOException or SocketException or ObjectDisposedException)
        {
            DispatchDisconnected(ex.Message);
        }
    }

    private void DispatchPacket(Packet packet)
    {
        var handler = PacketReceived;
        if (handler == null) return;

        if (_uiContext == null)
        {
            handler.Invoke(packet);
            return;
        }

        _uiContext.Post(_ =>
        {
            try { handler.Invoke(packet); }
            catch (Exception ex)
            {
                DispatchDisconnected($"Packet handler error: {ex.Message}");
            }
        }, null);
    }

    private void DispatchDisconnected(string reason)
    {
        var handler = Disconnected;
        if (handler == null) return;

        if (_uiContext == null)
        {
            handler.Invoke(reason);
            return;
        }

        _uiContext.Post(_ =>
        {
            try { handler.Invoke(reason); }
            catch { /* Do not let a UI notification error crash the receive loop. */ }
        }, null);
    }

    public void Close()
    {
        try { _tcp?.Close(); } catch { }
    }
}
