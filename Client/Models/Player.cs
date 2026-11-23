namespace Client.Models;

public sealed class Player
{
    public string Username { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public string Realm { get; set; } = "Luyện Khí Tầng 1";
    public string SectTitle { get; set; } = "Ký Danh Đệ Tử";
    public int SpiritStones { get; set; } = 10;
    public bool IsMeditating { get; set; }
}
