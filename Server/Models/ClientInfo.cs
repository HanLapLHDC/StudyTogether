using System.Net.Sockets;

namespace Server.Models;

public sealed class ClientInfo
{
    public string Username { get; set; } = "";
    public string RoomName { get; set; } = "";
    public TcpClient Tcp { get; set; } = null!;
    public StreamWriter Writer { get; set; } = null!;
    public int X { get; set; } = 320;
    public int Y { get; set; } = 280;
    public string Realm { get; set; } = "Luyện Khí Tầng 1";
    public int SpiritStones { get; set; } = 10;
    public bool IsMeditating { get; set; }
    public SemaphoreSlim SendLock { get; } = new(1, 1);
}
