namespace Client.Models;

public sealed class Packet
{
    public string Type { get; set; } = "";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? RoomName { get; set; }
    public string? Text { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public bool Success { get; set; }
    public string? Realm { get; set; }
    public string? SectRole { get; set; }
    public int SpiritStones { get; set; }
    public bool IsMeditating { get; set; }
}
