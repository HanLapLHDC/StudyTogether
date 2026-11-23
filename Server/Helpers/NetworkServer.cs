using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Server.Models;

namespace Server.Helpers;

public sealed class NetworkServer
{
    private readonly ConcurrentDictionary<TcpClient, ClientInfo> _clients = new();
    private readonly Dictionary<string, string> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _accountLock = new();
    private readonly object _roomLock = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public Action<string>? Log { get; set; }
    public Action<int>? ClientCountChanged { get; set; }
    public bool IsRunning => _listener != null;

    public async Task StartAsync(int port)
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Log?.Invoke($"Hộ Sơn Đại Trận đã mở tại cổng {port}.");
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient tcp = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => HandleClientAsync(tcp));
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (_cts?.IsCancellationRequested == true) { }
        finally
        {
            _listener?.Stop();
            _listener = null;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
        foreach (var c in _clients.Keys)
            try { c.Close(); } catch { }
        Log?.Invoke("Hộ Sơn Đại Trận đã đóng.");
    }

    private async Task HandleClientAsync(TcpClient tcp)
    {
        ClientInfo? info = null;
        try
        {
            using NetworkStream stream = tcp.GetStream();
            using var reader = new StreamReader(stream);
            var writer = new StreamWriter(stream) { AutoFlush = true };
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                Packet? packet = JsonSerializer.Deserialize<Packet>(line);
                if (packet == null) continue;
                if (packet.Type == "REGISTER")
                {
                    bool ok;
                    lock (_accountLock)
                    {
                        string user = (packet.Username ?? "").Trim();
                        ok = user.Length >= 3 && (packet.Password ?? "").Length >= 3 && !_accounts.ContainsKey(user);
                        if (ok) _accounts[user] = packet.Password!;
                    }
                    await SendAsync(writer, new Packet { Type = "REGISTER_RESULT", Success = ok, Text = ok ? "Ghi danh nhập môn thành công! Hãy bước vào sơn môn." : "Đạo hiệu đã tồn tại hoặc mật khẩu quá ngắn (tối thiểu 3 ký tự)." });
                }
                else if (packet.Type == "LOGIN")
                {
                    bool ok;
                    string user = (packet.Username ?? "").Trim();
                    lock (_accountLock)
                        ok = _accounts.TryGetValue(user, out string? pass) && pass == packet.Password;
                    if (!ok)
                    {
                        await SendAsync(writer, new Packet { Type = "LOGIN_RESULT", Success = false, Text = "Đạo hiệu hoặc tâm quyết không đúng. Hãy ghi danh nhập môn trước." });
                        continue;
                    }
                    if (info != null) continue;
                    info = new ClientInfo { Username = user, Tcp = tcp, Writer = writer };
                    _clients[tcp] = info;
                    ClientCountChanged?.Invoke(_clients.Count);
                    Log?.Invoke($"Đạo hữu {user} đã bước vào Hoàng Phong Cốc.");
                    await SendAsync(writer, new Packet { Type = "LOGIN_RESULT", Success = true, Username = user, Realm = info.Realm, SpiritStones = info.SpiritStones, Text = "Nhập môn thành công." }, info);
                    await SendRoomListAsync(info);
                }
                else if (info == null)
                {
                    await SendAsync(writer, new Packet { Type = "ERROR", Text = "Vui lòng nhập môn trước." });
                }
                else
                {
                    await ProcessPacketAsync(info, packet);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or JsonException)
        {
            Log?.Invoke($"Ngắt kết nối thần thức: {ex.Message}");
        }
        finally
        {
            if (info != null)
            {
                _clients.TryRemove(tcp, out _);
                if (!string.IsNullOrEmpty(info.RoomName))
                {
                    await BroadcastRoomAsync(info.RoomName, new Packet { Type = "SYSTEM", Text = $"Đạo hữu {info.Username} đã rời khỏi động phủ." }, info);
                    await BroadcastRoomAsync(info.RoomName, new Packet { Type = "PLAYER_LEFT", Username = info.Username }, info);
                }
                Log?.Invoke($"Đạo hữu {info.Username} đã rời tông môn.");
                ClientCountChanged?.Invoke(_clients.Count);
            }
            try { tcp.Close(); } catch { }
        }
    }

    private async Task ProcessPacketAsync(ClientInfo info, Packet p)
    {
        switch (p.Type)
        {
            case "CREATE_ROOM":
                string newRoom = (p.RoomName ?? "").Trim();
                if (newRoom.Length < 2 || newRoom.Length > 30)
                {
                    await SendAsync(info.Writer, new Packet { Type = "ACTION_RESULT", Success = false, Text = "Tên động phủ / giảng đường phải từ 2 đến 30 ký tự." }, info);
                    break;
                }
                bool created;
                lock (_roomLock)
                {
                    created = !RoomExists(newRoom);
                    if (created)
                        _rooms.Add(newRoom, new List<ClientInfo>());
                }

                Log?.Invoke(created
                    ? $"KHAI MỞ ĐỘNG PHỦ: {newRoom} bởi {info.Username}"
                    : $"ĐỘNG PHỦ ĐÃ TỒN TẠI: {newRoom}");

                await JoinRoomAsync(info, newRoom);
                await SendRoomListsToAllAsync();
                break;
            case "JOIN_ROOM":
                await JoinRoomAsync(info, (p.RoomName ?? "").Trim());
                await SendRoomListsToAllAsync();
                break;
            case "LEAVE_ROOM":
                await LeaveRoomAsync(info);
                break;
            case "ROOM_LIST":
                await SendRoomListAsync(info);
                break;
            case "CHAT":
                if (string.IsNullOrEmpty(info.RoomName))
                {
                    await SendAsync(info.Writer, new Packet
                    {
                        Type = "ACTION_RESULT",
                        Success = false,
                        Text = "Hãy tiến vào một động phủ trước khi truyền âm luận đạo."
                    }, info);
                    break;
                }

                string msg = (p.Text ?? "").Trim();
                if (msg.Length == 0) break;

                Log?.Invoke($"TRUYỀN ÂM [{info.RoomName}] {info.Username}: {msg}");
                await BroadcastRoomAsync(info.RoomName,
                    new Packet { Type = "CHAT", Username = info.Username, Realm = info.Realm, Text = msg });
                break;
            case "MOVE":
                info.X = Math.Clamp(p.X, 40, 660);
                info.Y = Math.Clamp(p.Y, 170, 400);
                info.IsMeditating = p.IsMeditating;
                if (!string.IsNullOrEmpty(info.RoomName))
                    await BroadcastRoomAsync(info.RoomName, new Packet
                    {
                        Type = "PLAYER_MOVED",
                        Username = info.Username,
                        X = info.X,
                        Y = info.Y,
                        Realm = info.Realm,
                        IsMeditating = info.IsMeditating
                    });
                break;
            case "CULTIVATE_STATE":
                if (!string.IsNullOrWhiteSpace(p.Realm)) info.Realm = p.Realm;
                if (p.SpiritStones > 0) info.SpiritStones = p.SpiritStones;
                info.IsMeditating = p.IsMeditating;
                if (!string.IsNullOrEmpty(info.RoomName))
                {
                    await BroadcastRoomAsync(info.RoomName, new Packet
                    {
                        Type = "CULTIVATE_STATE",
                        Username = info.Username,
                        Realm = info.Realm,
                        SpiritStones = info.SpiritStones,
                        IsMeditating = info.IsMeditating,
                        X = info.X,
                        Y = info.Y,
                        Text = p.Text
                    });
                }
                break;
            case "POMODORO":
                if (!string.IsNullOrWhiteSpace(p.Realm)) info.Realm = p.Realm;
                if (p.SpiritStones > 0) info.SpiritStones = p.SpiritStones;
                if (!string.IsNullOrEmpty(info.RoomName))
                    await BroadcastRoomAsync(info.RoomName, new Packet
                    {
                        Type = "POMODORO",
                        Username = info.Username,
                        Realm = info.Realm,
                        SpiritStones = info.SpiritStones,
                        Text = p.Text
                    });
                break;
        }
    }

    private readonly Dictionary<string, List<ClientInfo>> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private bool RoomExists(string name) => _rooms.ContainsKey(name);

    private async Task JoinRoomAsync(ClientInfo info, string room)
    {
        if (room.Length == 0)
        {
            await SendAsync(info.Writer, new Packet { Type = "ACTION_RESULT", Success = false, Text = "Tên động phủ không hợp lệ." }, info);
            return;
        }

        await LeaveRoomAsync(info);
        List<ClientInfo>? members;
        string? error = null;
        lock (_roomLock)
        {
            if (!_rooms.TryGetValue(room, out members))
                error = "Động phủ này chưa được khai mở.";
            else if (members.Count >= 20)
                error = "Động phủ đã đầy (tối đa 20 tu sĩ).";
            else
            {
                members.Add(info);
                info.RoomName = room;
                // Position disciples onto meditation cushions
                int idx = members.Count - 1;
                int col = idx % 5;
                int row = idx / 5;
                info.X = 160 + col * 95;
                info.Y = 250 + row * 55;
            }
        }
        if (error != null)
        {
            await SendAsync(info.Writer, new Packet { Type = "ACTION_RESULT", Success = false, Text = error }, info);
            return;
        }

        await SendAsync(info.Writer, new Packet
        {
            Type = "JOINED_ROOM",
            RoomName = room,
            X = info.X,
            Y = info.Y,
            Realm = info.Realm,
            SpiritStones = info.SpiritStones,
            Text = "Ngươi đã tiến vào động phủ."
        }, info);
        Log?.Invoke($"NHẬP ĐỘNG PHỦ: {info.Username} -> {room}");
        foreach (var member in members.Where(m => m != info))
        {
            await SendAsync(info.Writer, new Packet
            {
                Type = "PLAYER_JOINED",
                Username = member.Username,
                X = member.X,
                Y = member.Y,
                Realm = member.Realm,
                IsMeditating = member.IsMeditating
            });
            await SendAsync(member.Writer, new Packet
            {
                Type = "PLAYER_JOINED",
                Username = info.Username,
                X = info.X,
                Y = info.Y,
                Realm = info.Realm,
                IsMeditating = info.IsMeditating
            });
        }
        await BroadcastRoomAsync(room, new Packet { Type = "SYSTEM", Text = $"Đạo hữu [{info.Realm}] {info.Username} đã giá lâm động phủ." }, info);
        await SendRoomListAsync(info);
    }

    private async Task LeaveRoomAsync(ClientInfo info)
    {
        string old = info.RoomName;
        if (string.IsNullOrEmpty(old)) return;
        lock (_roomLock)
        {
            if (_rooms.TryGetValue(old, out var list))
                list.Remove(info);
            info.RoomName = "";
        }
        await BroadcastRoomAsync(old, new Packet { Type = "SYSTEM", Text = $"Đạo hữu {info.Username} đã rời khỏi động phủ." }, info);
        await BroadcastRoomAsync(old, new Packet { Type = "PLAYER_LEFT", Username = info.Username }, info);
    }

    private async Task SendRoomListAsync(ClientInfo client)
    {
        string[] names;
        lock (_roomLock) names = _rooms.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

        string roomList = string.Join("|", names);
        await SendAsync(client.Writer,
            new Packet { Type = "ROOM_LIST_RESULT", Text = roomList }, client);
    }

    private async Task SendRoomListsToAllAsync()
    {
        ClientInfo[] clients = _clients.Values.ToArray();
        foreach (ClientInfo client in clients)
        {
            try { await SendRoomListAsync(client); }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                Log?.Invoke($"Gửi danh sách động phủ thất bại cho {client.Username}: {ex.Message}");
            }
        }
    }

    private async Task BroadcastRoomAsync(string room, Packet packet, ClientInfo? except = null)
    {
        ClientInfo[] targets;
        lock (_roomLock)
            targets = _rooms.TryGetValue(room, out var list) ? list.Where(c => c != except).ToArray() : Array.Empty<ClientInfo>();
        foreach (var target in targets)
        {
            try
            {
                await SendAsync(target.Writer, packet, target);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                Log?.Invoke($"Gửi gói tin {packet.Type} thất bại tới {target.Username}: {ex.Message}");
            }
        }
    }

    private static async Task SendAsync(StreamWriter writer, Packet packet, ClientInfo? info = null)
    {
        string json = JsonSerializer.Serialize(packet);
        if (info == null)
        {
            await writer.WriteLineAsync(json);
            return;
        }
        await info.SendLock.WaitAsync();
        try { await writer.WriteLineAsync(json); }
        finally { info.SendLock.Release(); }
    }
}
