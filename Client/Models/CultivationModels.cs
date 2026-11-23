namespace Client.Models;

public sealed class CultivationRealm
{
    public int Index { get; }
    public string Name { get; }
    public string Title { get; }
    public int RequiredFocusMinutes { get; }
    public Color AuraColor { get; }
    public Color RobeColor { get; }

    public CultivationRealm(int index, string name, string title, int requiredFocusMinutes, Color auraColor, Color robeColor)
    {
        Index = index;
        Name = name;
        Title = title;
        RequiredFocusMinutes = requiredFocusMinutes;
        AuraColor = auraColor;
        RobeColor = robeColor;
    }
}

public sealed class SectMentor
{
    public string Name { get; }
    public string Title { get; }
    public string Realm { get; }
    public float RelX { get; }
    public float RelY { get; }
    public Color RobeColor { get; }
    public Color AuraColor { get; }
    public string[] Lectures { get; }

    public SectMentor(string name, string title, string realm, float relX, float relY, Color robeColor, Color auraColor, string[] lectures)
    {
        Name = name;
        Title = title;
        Realm = realm;
        RelX = relX;
        RelY = relY;
        RobeColor = robeColor;
        AuraColor = auraColor;
        Lectures = lectures;
    }
}

public sealed class SpiritualObject
{
    public string Name { get; }
    public string Description { get; }
    public float RelX { get; }
    public float RelY { get; }
    public string Kind { get; } // "Cushion", "Cauldron", "Stele", "Lamp", "Brazier"

    public SpiritualObject(string name, string description, float relX, float relY, string kind)
    {
        Name = name;
        Description = description;
        RelX = relX;
        RelY = relY;
        Kind = kind;
    }
}

public sealed class SpiritParticle
{
    public float X { get; set; }
    public float Y { get; set; }
    public float SpeedY { get; set; }
    public float DriftX { get; set; }
    public float Size { get; set; }
    public int Alpha { get; set; }
    public Color Tint { get; set; }
}

public static class MortalCultivationLore
{
    public static readonly IReadOnlyList<CultivationRealm> Realms = new List<CultivationRealm>
    {
        new(0, "Luyện Khí Tầng 1", "Ký Danh Đệ Tử", 0, Color.FromArgb(130, 210, 180), Color.FromArgb(62, 92, 82)),
        new(1, "Luyện Khí Tầng 4", "Ngoại Môn Đệ Tử", 25, Color.FromArgb(110, 225, 195), Color.FromArgb(50, 108, 92)),
        new(2, "Luyện Khí Viên Mãn", "Nội Môn Tinh Anh", 50, Color.FromArgb(95, 220, 240), Color.FromArgb(44, 96, 128)),
        new(3, "Trúc Cơ Kỳ", "Trúc Cơ Trưởng Lão", 100, Color.FromArgb(120, 185, 255), Color.FromArgb(52, 78, 138)),
        new(4, "Kết Đan Kỳ", "Kim Đan Chân Nhân", 200, Color.FromArgb(245, 200, 95), Color.FromArgb(138, 96, 42)),
        new(5, "Nguyên Anh Kỳ", "Nguyên Anh Lão Tổ", 350, Color.FromArgb(215, 140, 255), Color.FromArgb(96, 54, 126)),
        new(6, "Hóa Thần Kỳ", "Hóa Thần Đại Năng", 500, Color.FromArgb(255, 235, 150), Color.FromArgb(165, 125, 48))
    };

    public static CultivationRealm GetRealmByMinutes(int totalMinutes)
    {
        CultivationRealm current = Realms[0];
        foreach (var r in Realms)
        {
            if (totalMinutes >= r.RequiredFocusMinutes)
                current = r;
        }
        return current;
    }

    public static CultivationRealm? GetNextRealm(CultivationRealm current)
    {
        int nextIdx = current.Index + 1;
        return nextIdx < Realms.Count ? Realms[nextIdx] : null;
    }

    public static readonly IReadOnlyList<SectMentor> Mentors = new List<SectMentor>
    {
        new("HÀN LẬP (Hàn Lão Ma)", "Chưởng Tọa Truyền Công", "Nguyên Anh Hậu Kỳ", 0.50f, 0.30f,
            Color.FromArgb(42, 76, 66), Color.FromArgb(110, 235, 185),
            new[]
            {
                "“Tu tiên chi lộ, nghịch thiên nhi hành. Tâm tĩnh như cổ tỉnh, kiến thức mới nhập thần thức.”",
                "“Không có linh căn thượng phẩm thì dùng cần cù bù đắp. Mỗi nén nhang tĩnh tọa đều là một bước tiến.”",
                "“Gặp nan đề chớ vội vàng, hãy vận chuyển Thanh Nguyên Kiếm Quyết, từng bước phá giải!”",
                "“Trong Chưởng Thiên Bình có tạo hóa, trong từng trang sách có đại đạo.”"
            }),
        new("NAM CUNG UYỂN", "Yểm Nguyệt Tông Trưởng Lão", "Kết Đan Hậu Kỳ", 0.23f, 0.36f,
            Color.FromArgb(72, 102, 138), Color.FromArgb(145, 205, 255),
            new[]
            {
                "“Tố Nữ Luân Hồi Công chú trọng điều hòa tâm tức. Học tập mệt mỏi hãy nghỉ ngơi 5 phút dưỡng thần.”",
                "“Tâm ma sinh ra từ sự xao nhãng. Đã ngồi lên bồ đoàn thì chớ để ngoại vật quấy nhiễu.”",
                "“Đạo tâm kiên định, tự nhiên thủy đáo cừ thành.”"
            }),
        new("TỬ LINH TIÊN TỬ", "Diệu Âm Môn Chủ", "Kết Đan Sơ Kỳ", 0.77f, 0.36f,
            Color.FromArgb(128, 72, 110), Color.FromArgb(240, 165, 220),
            new[]
            {
                "“Thiên Nam tu tiên giới rộng lớn, đồng đạo cùng nhau luận bàn sẽ tiến bộ nhanh gấp bội.”",
                "“Linh khí trong động phủ đang tụ hội, các vị đạo hữu hãy mau nhập định tu luyện!”",
                "“Mỗi lần hoàn thành một chu kỳ tọa quan sẽ nhận thêm Linh Thạch hộ thân.”"
            })
    };

    public static readonly IReadOnlyList<SpiritualObject> ClassroomObjects = new List<SpiritualObject>
    {
        new("Bồ Đoàn Tụ Linh", "Ngồi lên bồ đoàn và bấm Tọa Quan Nhập Định để hấp thu thiên địa linh khí.", 0.32f, 0.62f, "Cushion"),
        new("Bồ Đoàn Tụ Linh", "Ngồi lên bồ đoàn và bấm Tọa Quan Nhập Định để hấp thu thiên địa linh khí.", 0.50f, 0.62f, "Cushion"),
        new("Bồ Đoàn Tụ Linh", "Ngồi lên bồ đoàn và bấm Tọa Quan Nhập Định để hấp thu thiên địa linh khí.", 0.68f, 0.62f, "Cushion"),
        new("Bồ Đoàn Tụ Linh", "Ngồi lên bồ đoàn và bấm Tọa Quan Nhập Định để hấp thu thiên địa linh khí.", 0.41f, 0.77f, "Cushion"),
        new("Bồ Đoàn Tụ Linh", "Ngồi lên bồ đoàn và bấm Tọa Quan Nhập Định để hấp thu thiên địa linh khí.", 0.59f, 0.77f, "Cushion"),
        new("Hư Thiên Đỉnh", "Linh đỉnh trấn giữ giảng đường, tỏa ra tử khí ngưng thần tĩnh tâm.", 0.14f, 0.55f, "Cauldron"),
        new("Thanh Nguyên Kiếm Bia", "Thạch bi khắc tâm pháp Thanh Nguyên Kiếm Quyết của Hàn Lão Ma.", 0.86f, 0.55f, "Stele"),
        new("Chưởng Thiên Bình Đài", "Linh đài ngưng tụ Lục Dịch, gia trì linh khí cho toàn thể đệ tử.", 0.50f, 0.44f, "Pedestal")
    };
}
