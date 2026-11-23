using Client.Helpers;
using Client.Models;

namespace Client;

public sealed class LoginForm : Form
{
    private readonly TextBox _host = new() { Text = "127.0.0.1" };
    private readonly NumericUpDown _port = new() { Minimum = 1024, Maximum = 65535, Value = 9000 };
    private readonly TextBox _username = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly Label _status = new();
    private readonly NetworkClient _network = new();
    private TaskCompletionSource<Packet>? _pending;

    public LoginForm()
    {
        Text = "Hoàng Phong Cốc - Sơn Môn Nhập Đạo";
        Width = 540;
        Height = 600;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(239, 231, 207);
        Font = new Font("Segoe UI", 11F);

        var title = new Label
        {
            Text = "HOÀNG PHONG CỐC",
            Font = new Font("Georgia", 23F, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 74, 60),
            AutoSize = true,
            Left = 92,
            Top = 28
        };
        var subtitle = new Label
        {
            Text = "Phàm Nhân Tu Tiên • Truyền Công Giảng Đường",
            Font = new Font("Georgia", 11F, FontStyle.Italic),
            ForeColor = Color.FromArgb(125, 94, 54),
            AutoSize = true,
            Left = 88,
            Top = 76
        };

        AddLabel("TRẬN PHÁP IP", 55, 142);
        _host.SetBounds(200, 136, 265, 38);
        AddLabel("CỔNG LINH TRẬN", 55, 202);
        _port.SetBounds(200, 196, 265, 38);
        AddLabel("ĐẠO HIỆU", 55, 262);
        _username.SetBounds(200, 256, 265, 38);
        _username.PlaceholderText = "VD: HanLap, LePhiVu...";
        AddLabel("TÂM QUYẾT", 55, 322);
        _password.SetBounds(200, 316, 265, 38);

        var login = MakeButton("BƯỚC VÀO SƠN MÔN", Color.FromArgb(45, 105, 87));
        login.SetBounds(55, 390, 410, 52);
        var register = MakeButton("GHI DANH NHẬP MÔN (ĐĂNG KÝ)", Color.FromArgb(112, 83, 48));
        register.SetBounds(55, 455, 410, 52);

        _status.SetBounds(45, 520, 440, 36);
        _status.ForeColor = Color.DarkRed;
        _status.Font = new Font("Segoe UI", 9.5F);

        Controls.AddRange([title, subtitle, _host, _port, _username, _password, login, register, _status]);
        login.Click += async (_, _) => await AuthenticateAsync(false);
        register.Click += async (_, _) => await AuthenticateAsync(true);
        AcceptButton = login;
    }

    private void AddLabel(string text, int x, int y) =>
        Controls.Add(new Label
        {
            Text = text,
            Left = x,
            Top = y + 8,
            Width = 140,
            Height = 28,
            ForeColor = Color.FromArgb(65, 57, 43),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
        });

    private static Button MakeButton(string text, Color color) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
        ForeColor = Color.White,
        BackColor = color,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand
    };

    private async Task AuthenticateAsync(bool register)
    {
        string user = _username.Text.Trim();
        string pass = _password.Text;

        if (user.Length < 3 || pass.Length < 3)
        {
            _status.Text = "Đạo hiệu và tâm quyết phải có ít nhất 3 ký tự.";
            return;
        }

        try
        {
            if (!_network.Connected)
            {
                await _network.ConnectAsync(_host.Text.Trim(), (int)_port.Value);
                _network.PacketReceived += HandlePacket;
            }

            _pending = new TaskCompletionSource<Packet>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _network.SendAsync(new Packet
            {
                Type = register ? "REGISTER" : "LOGIN",
                Username = user,
                Password = pass
            });

            Packet result = await _pending.Task.WaitAsync(TimeSpan.FromSeconds(8));
            _status.ForeColor = result.Success ? Color.DarkGreen : Color.DarkRed;
            _status.Text = result.Text ?? "";
            if (register) return;

            if (result.Success)
            {
                _network.PacketReceived -= HandlePacket;
                Hide();
                var main = new StudyForm(_network, user);
                main.FormClosed += (_, _) => Close();
                main.Show();
            }
        }
        catch (Exception ex)
        {
            _status.ForeColor = Color.DarkRed;
            _status.Text = "Kết nối linh trận thất bại: " + ex.Message;
        }
    }

    private void HandlePacket(Packet packet)
    {
        if (packet.Type is "LOGIN_RESULT" or "REGISTER_RESULT")
            _pending?.TrySetResult(packet);
    }
}
