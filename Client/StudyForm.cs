using Client.Helpers;
using Client.Models;
using System.Drawing.Drawing2D;

namespace Client;

public sealed class DoubleBufferedPanel : Panel
{
    public DoubleBufferedPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }
}

public sealed class StudyForm : Form
{
    private readonly NetworkClient _network;
    private readonly string _username;
    private readonly ListBox _rooms = new();
    private readonly ListBox _chat = new();
    private readonly TextBox _message = new();
    private readonly TextBox _roomName = new();
    private readonly Label _roomTitle = new();
    private readonly Label _roomSubtitle = new();
    private readonly Label _welcomeLabel = new();
    private readonly Label _realmBadge = new();
    private readonly Label _stoneLabel = new();
    private readonly ProgressBar _cultivationBar = new();
    private readonly Label _timerLabel = new();
    private readonly Label _timerCaption = new();
    private readonly Button _meditateBtn = new();
    private readonly DoubleBufferedPanel _world = new();
    private readonly Dictionary<string, Player> _players = new();
    private readonly List<SpiritParticle> _particles = new();
    private readonly Random _rng = new();

    private readonly System.Windows.Forms.Timer _pomodoro = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _animTimer = new() { Interval = 45 };

    private int _seconds = 25 * 60;
    private int _sessionDurationSeconds = 25 * 60;
    private int _totalCultivatedMinutes = 0;
    private int _spiritStones = 10;
    private CultivationRealm _currentRealm = MortalCultivationLore.Realms[0];

    private int _x = 340, _y = 290;
    private bool _timerRunning;
    private bool _isMeditating;
    private float _animTick;
    private string _activeBubbleText = "";
    private string _activeBubbleSpeaker = "";
    private int _bubbleTicksLeft;

    private static readonly Color Ink = Color.FromArgb(42, 39, 31);
    private static readonly Color Jade = Color.FromArgb(40, 102, 82);
    private static readonly Color Gold = Color.FromArgb(196, 155, 72);
    private static readonly Color Paper = Color.FromArgb(245, 239, 220);
    private static readonly Color PanelBrown = Color.FromArgb(82, 61, 41);

    public StudyForm(NetworkClient network, string username)
    {
        _network = network;
        _username = username;

        Text = $"Hoàng Phong Cốc - Truyền Công Giảng Đường | Đạo hữu {_username}";
        Width = 1480;
        Height = 920;
        MinimumSize = new Size(1160, 740);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(24, 36, 33);
        Font = new Font("Segoe UI", 10F);

        InitSpiritParticles();
        BuildUi();
        UpdateCultivationStatus();

        _network.PacketReceived += HandlePacket;
        _network.Disconnected += reason => Ui(() =>
            MessageBox.Show(this, reason, "Thần Thức Gián Đoạn", MessageBoxButtons.OK, MessageBoxIcon.Warning));

        _animTimer.Tick += (_, _) =>
        {
            _animTick += 0.14f;
            UpdateParticles();
            if (_bubbleTicksLeft > 0)
            {
                _bubbleTicksLeft--;
                if (_bubbleTicksLeft == 0) _activeBubbleText = "";
            }
            _world.Invalidate();
        };
        _animTimer.Start();

        _pomodoro.Tick += async (_, _) =>
        {
            if (_seconds > 0) _seconds--;
            UpdateTimer();

            if (_seconds == 0)
            {
                _timerRunning = false;
                _isMeditating = false;
                _pomodoro.Stop();
                _meditateBtn.Text = "TỌA QUAN NHẬP ĐỊNH";
                _meditateBtn.BackColor = Jade;

                int earnedMinutes = Math.Max(1, _sessionDurationSeconds / 60);
                int earnedStones = earnedMinutes >= 25 ? 15 : 3;
                _totalCultivatedMinutes += earnedMinutes;
                _spiritStones += earnedStones;

                var oldRealm = _currentRealm;
                _currentRealm = MortalCultivationLore.GetRealmByMinutes(_totalCultivatedMinutes);
                bool breakthrough = _currentRealm.Index > oldRealm.Index;

                UpdateCultivationStatus();

                if (_players.TryGetValue(_username, out var me))
                {
                    me.Realm = _currentRealm.Name;
                    me.SpiritStones = _spiritStones;
                    me.IsMeditating = false;
                }

                string notice = breakthrough
                    ? $"Chúc mừng đạo hữu {_username} linh khí viên mãn, ĐỘT PHÁ thành công tới [{_currentRealm.Name}]! Nhận {earnedStones} Hạ Phẩm Linh Thạch."
                    : $"Hoàn thành một chu kỳ tọa quan ({earnedMinutes} phút)! Hấp thu thiên địa linh khí, nhận {earnedStones} Hạ Phẩm Linh Thạch.";

                TriggerSpeechBubble("CHƯỞNG THIÊN BÌNH", breakthrough
                    ? $"Linh quang vạn trượng! {_username} đột phá {_currentRealm.Name}!"
                    : $"{_username} hoàn thành tọa quan, đạo tâm vững chắc!");

                MessageBox.Show(this, notice, breakthrough ? "Đột Phá Cảnh Giới!" : "Tọa Quan Viên Mãn",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                try
                {
                    await _network.SendAsync(new Packet
                    {
                        Type = "POMODORO",
                        Realm = _currentRealm.Name,
                        SpiritStones = _spiritStones,
                        Text = breakthrough
                            ? $"vừa dẫn động linh khí thiên địa, ĐỘT PHÁ cảnh giới [{_currentRealm.Name}]!"
                            : $"đã hoàn thành một nén nhang tọa quan (+{earnedStones} Linh Thạch)."
                    });
                }
                catch { }
            }
        };

        Shown += async (_, _) =>
        {
            try { await _network.SendAsync(new Packet { Type = "ROOM_LIST" }); }
            catch (Exception ex) { ShowNetworkError(ex); }
        };

        FormClosing += (_, _) =>
        {
            _pomodoro.Stop();
            _animTimer.Stop();
            _network.Close();
        };

        KeyPreview = true;
        KeyDown += async (_, e) =>
        {
            if (ActiveControl is TextBoxBase) return;

            if (e.KeyCode == Keys.Space)
            {
                e.SuppressKeyPress = true;
                InteractWithNearestEntity();
                return;
            }

            const int step = 14;
            bool moved = true;

            if (e.KeyCode == Keys.W || e.KeyCode == Keys.Up) _y -= step;
            else if (e.KeyCode == Keys.S || e.KeyCode == Keys.Down) _y += step;
            else if (e.KeyCode == Keys.A || e.KeyCode == Keys.Left) _x -= step;
            else if (e.KeyCode == Keys.D || e.KeyCode == Keys.Right) _x += step;
            else moved = false;

            if (!moved) return;

            // Moving interrupts seated meditation pose
            if (_isMeditating && !_timerRunning)
                _isMeditating = false;

            int maxW = Math.Max(660, _world.ClientSize.Width - 35);
            int maxH = Math.Max(400, _world.ClientSize.Height - 45);
            _x = Math.Clamp(_x, 35, maxW);
            _y = Math.Clamp(_y, 165, maxH);

            _players[_username] = new Player
            {
                Username = _username,
                X = _x,
                Y = _y,
                Realm = _currentRealm.Name,
                SpiritStones = _spiritStones,
                IsMeditating = _isMeditating
            };
            _world.Invalidate();

            try
            {
                await _network.SendAsync(new Packet
                {
                    Type = "MOVE",
                    X = _x,
                    Y = _y,
                    Realm = _currentRealm.Name,
                    IsMeditating = _isMeditating
                });
            }
            catch { }
        };
    }

    private void InitSpiritParticles()
    {
        for (int i = 0; i < 36; i++)
        {
            _particles.Add(new SpiritParticle
            {
                X = _rng.Next(20, 800),
                Y = _rng.Next(20, 500),
                SpeedY = 0.35f + (float)_rng.NextDouble() * 0.75f,
                DriftX = ((float)_rng.NextDouble() - 0.5f) * 0.6f,
                Size = 2.5f + (float)_rng.NextDouble() * 3.5f,
                Alpha = _rng.Next(90, 210),
                Tint = i % 3 == 0 ? Color.FromArgb(245, 215, 115) : Color.FromArgb(120, 245, 195)
            });
        }
    }

    private void UpdateParticles()
    {
        int w = Math.Max(400, _world.ClientSize.Width);
        int h = Math.Max(300, _world.ClientSize.Height);
        foreach (var pt in _particles)
        {
            pt.Y -= pt.SpeedY * (_isMeditating ? 1.8f : 1.0f);
            pt.X += pt.DriftX + (float)Math.Sin(_animTick + pt.Y * 0.03f) * 0.35f;
            if (pt.Y < 10)
            {
                pt.Y = h - 10;
                pt.X = _rng.Next(15, w - 15);
            }
            if (pt.X < 10) pt.X = w - 15;
            if (pt.X > w - 10) pt.X = 15;
        }
    }

    private void BuildUi()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = Color.FromArgb(26, 48, 41),
            Padding = new Padding(22, 8, 22, 8)
        };

        var title = new Label
        {
            Text = "HOÀNG PHONG CỐC • TRUYỀN CÔNG GIẢNG ĐƯỜNG",
            Dock = DockStyle.Left,
            Width = 650,
            ForeColor = Color.FromArgb(238, 208, 142),
            Font = new Font("Georgia", 18.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var statusBox = new Panel
        {
            Dock = DockStyle.Right,
            Width = 460
        };

        _welcomeLabel.Text = $"ĐẠO HIỆU: {_username.ToUpperInvariant()}";
        _welcomeLabel.Dock = DockStyle.Top;
        _welcomeLabel.Height = 26;
        _welcomeLabel.ForeColor = Color.FromArgb(235, 228, 205);
        _welcomeLabel.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        _welcomeLabel.TextAlign = ContentAlignment.MiddleRight;

        _realmBadge.Dock = DockStyle.Top;
        _realmBadge.Height = 22;
        _realmBadge.ForeColor = Color.FromArgb(130, 235, 195);
        _realmBadge.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        _realmBadge.TextAlign = ContentAlignment.MiddleRight;

        _stoneLabel.Dock = DockStyle.Fill;
        _stoneLabel.ForeColor = Color.FromArgb(245, 210, 115);
        _stoneLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _stoneLabel.TextAlign = ContentAlignment.MiddleRight;

        statusBox.Controls.Add(_stoneLabel);
        statusBox.Controls.Add(_realmBadge);
        statusBox.Controls.Add(_welcomeLabel);

        header.Controls.Add(statusBox);
        header.Controls.Add(title);
        Controls.Add(header);

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(32, 44, 39),
            Padding = new Padding(14),
            ColumnCount = 3,
            RowCount = 1
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21F));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53F));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));
        Controls.Add(main);
        main.BringToFront();

        BuildRoomsPanel(main);
        BuildClassroomPanel(main);
        BuildChatPanel(main);
    }

    private void BuildRoomsPanel(TableLayoutPanel parent)
    {
        var panel = CreateSectionPanel();
        panel.Padding = new Padding(14);
        parent.Controls.Add(panel, 0, 0);

        var heading = CreateSectionTitle("ĐỘNG PHỦ TÔNG MÔN");
        heading.Dock = DockStyle.Top;
        heading.Height = 38;
        panel.Controls.Add(heading);

        var description = new Label
        {
            Text = "Chọn linh sơn động phủ để cùng đồng đạo bế quan tu luyện.",
            Dock = DockStyle.Top,
            Height = 46,
            ForeColor = Color.FromArgb(105, 96, 78),
            Font = new Font("Segoe UI", 9.5F)
        };
        panel.Controls.Add(description);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 224,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(0, 12, 0, 0)
        };
        for (int i = 0; i < 4; i++)
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));

        _roomName.Dock = DockStyle.Fill;
        _roomName.Font = new Font("Segoe UI", 10.5F);
        _roomName.PlaceholderText = "Tên động phủ (VD: Trúc Cơ Các)";
        _roomName.Margin = new Padding(0, 4, 0, 8);

        var create = MakeButton("KHAI MỞ ĐỘNG PHỦ", Jade);
        var join = MakeButton("NHẬP ĐỘNG PHỦ", PanelBrown);
        var refresh = MakeButton("LÀM MỚI LINH TRẬN", Color.FromArgb(126, 105, 66));

        actions.Controls.Add(_roomName, 0, 0);
        actions.Controls.Add(create, 0, 1);
        actions.Controls.Add(join, 0, 2);
        actions.Controls.Add(refresh, 0, 3);
        panel.Controls.Add(actions);

        _rooms.Dock = DockStyle.Fill;
        _rooms.Font = new Font("Segoe UI", 11F);
        _rooms.BackColor = Color.FromArgb(250, 246, 232);
        _rooms.ForeColor = Ink;
        _rooms.BorderStyle = BorderStyle.FixedSingle;
        panel.Controls.Add(_rooms);
        _rooms.BringToFront();

        create.Click += async (_, _) =>
        {
            string chamber = _roomName.Text.Trim();
            if (chamber.Length == 0)
            {
                MessageBox.Show(this, "Vui lòng nhập tên động phủ trước.", "Tông Môn Nhắc Nhở",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!_rooms.Items.Cast<string>().Contains(chamber, StringComparer.OrdinalIgnoreCase))
                _rooms.Items.Add(chamber);
            _rooms.SelectedItem = chamber;

            try
            {
                await _network.SendAsync(new Packet
                {
                    Type = "CREATE_ROOM",
                    RoomName = chamber
                });
            }
            catch (Exception ex)
            {
                int index = _rooms.Items.IndexOf(chamber);
                if (index >= 0) _rooms.Items.RemoveAt(index);
                ShowNetworkError(ex);
            }
        };

        join.Click += async (_, _) =>
        {
            if (_rooms.SelectedItem is not string chamber) return;
            try { await _network.SendAsync(new Packet { Type = "JOIN_ROOM", RoomName = chamber }); }
            catch (Exception ex) { ShowNetworkError(ex); }
        };

        refresh.Click += async (_, _) =>
        {
            try { await _network.SendAsync(new Packet { Type = "ROOM_LIST" }); }
            catch (Exception ex) { ShowNetworkError(ex); }
        };
    }

    private void BuildClassroomPanel(TableLayoutPanel parent)
    {
        var panel = CreateSectionPanel();
        panel.Padding = new Padding(14);
        parent.Controls.Add(panel, 1, 0);

        var top = new Panel { Dock = DockStyle.Top, Height = 64 };
        _roomTitle.Text = "TRUYỀN CÔNG ĐẠI ĐIỆN (CHƯA VÀO ĐỘNG PHỦ)";
        _roomTitle.Dock = DockStyle.Top;
        _roomTitle.Height = 32;
        _roomTitle.Font = new Font("Georgia", 14.5F, FontStyle.Bold);
        _roomTitle.ForeColor = Color.FromArgb(75, 57, 37);

        _roomSubtitle.Text = "Dùng W/A/S/D di chuyển • Nhấp vào Hàn Lập, Nam Cung Uyển, Tử Linh hoặc bấm SPACE để thỉnh giáo đạo pháp!";
        _roomSubtitle.Dock = DockStyle.Fill;
        _roomSubtitle.ForeColor = Color.FromArgb(105, 92, 68);
        _roomSubtitle.Font = new Font("Segoe UI", 9.2F);
        top.Controls.Add(_roomSubtitle);
        top.Controls.Add(_roomTitle);
        panel.Controls.Add(top);

        _world.Dock = DockStyle.Fill;
        _world.Margin = new Padding(0, 6, 0, 10);
        _world.BackColor = Color.FromArgb(224, 219, 192);
        _world.BorderStyle = BorderStyle.FixedSingle;
        _world.Cursor = Cursors.Hand;
        _world.Paint += DrawWorld;
        _world.MouseClick += HandleWorldClick;
        panel.Controls.Add(_world);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 152,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(0, 6, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31F));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var progressPanel = new Panel { Dock = DockStyle.Fill };
        var movement = new Label
        {
            Text = "LINH LỰC TÍCH LŨY:",
            Dock = DockStyle.Left,
            Width = 140,
            ForeColor = Color.FromArgb(94, 82, 61),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _cultivationBar.Dock = DockStyle.Fill;
        _cultivationBar.Margin = new Padding(4, 7, 4, 7);
        progressPanel.Controls.Add(_cultivationBar);
        progressPanel.Controls.Add(movement);
        footer.Controls.Add(progressPanel, 0, 0);
        footer.SetColumnSpan(progressPanel, 3);

        _timerCaption.Text = "NÉN NHANG TỌA QUAN";
        _timerCaption.Dock = DockStyle.Fill;
        _timerCaption.ForeColor = Color.FromArgb(102, 81, 49);
        _timerCaption.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

        _timerLabel.Dock = DockStyle.Fill;
        _timerLabel.Font = new Font("Georgia", 22F, FontStyle.Bold);
        _timerLabel.ForeColor = Jade;
        _timerLabel.TextAlign = ContentAlignment.MiddleLeft;

        var timerStack = new Panel { Dock = DockStyle.Fill };
        timerStack.Controls.Add(_timerLabel);
        timerStack.Controls.Add(_timerCaption);
        _timerCaption.SetBounds(0, 2, 200, 22);
        _timerLabel.SetBounds(0, 26, 200, 48);
        footer.Controls.Add(timerStack, 0, 1);

        var timerButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 6, 0, 0)
        };
        _meditateBtn.Text = "TỌA QUAN NHẬP ĐỊNH";
        _meditateBtn.Height = 44;
        _meditateBtn.Width = 175;
        _meditateBtn.Margin = new Padding(3, 4, 3, 4);
        _meditateBtn.Font = new Font("Segoe UI", 9.2F, FontStyle.Bold);
        _meditateBtn.ForeColor = Color.White;
        _meditateBtn.BackColor = Jade;
        _meditateBtn.FlatStyle = FlatStyle.Flat;
        _meditateBtn.Cursor = Cursors.Hand;

        var reset = MakeButton("ĐIỀU TỨC 25 PHÚT", PanelBrown);
        reset.Width = 175;
        timerButtons.Controls.Add(_meditateBtn);
        timerButtons.Controls.Add(reset);
        footer.Controls.Add(timerButtons, 1, 1);

        var rightActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(4, 6, 0, 0)
        };
        var breakButton = MakeButton("DƯỠNG THẦN 5 PHÚT", Color.FromArgb(126, 105, 66));
        breakButton.Width = 165;
        var lectureBtn = MakeButton("THỈNH GIÁO CHƯỞNG TỌA", Color.FromArgb(62, 95, 128));
        lectureBtn.Width = 165;
        rightActions.Controls.Add(breakButton);
        rightActions.Controls.Add(lectureBtn);
        footer.Controls.Add(rightActions, 2, 1);

        panel.Controls.Add(footer);
        _world.BringToFront();

        _meditateBtn.Click += async (_, _) =>
        {
            _timerRunning = !_timerRunning;
            _isMeditating = _timerRunning;
            if (_timerRunning)
            {
                _pomodoro.Start();
                _meditateBtn.Text = "TẠM DỪNG VẬN CÔNG";
                _meditateBtn.BackColor = Color.FromArgb(168, 92, 42);
                TriggerSpeechBubble(_username, "Ngưng thần tĩnh khí, vận chuyển Thanh Nguyên Kiếm Quyết...");
            }
            else
            {
                _pomodoro.Stop();
                _meditateBtn.Text = "TỌA QUAN NHẬP ĐỊNH";
                _meditateBtn.BackColor = Jade;
            }

            if (_players.TryGetValue(_username, out var me))
            {
                me.IsMeditating = _isMeditating;
            }
            _world.Invalidate();

            try
            {
                await _network.SendAsync(new Packet
                {
                    Type = "CULTIVATE_STATE",
                    Realm = _currentRealm.Name,
                    SpiritStones = _spiritStones,
                    IsMeditating = _isMeditating,
                    Text = _isMeditating ? "đang ngồi xếp bằng trên bồ đoàn vận công nhập định." : "đã thu công đứng dậy."
                });
            }
            catch { }
        };

        reset.Click += (_, _) =>
        {
            _pomodoro.Stop();
            _timerRunning = false;
            _isMeditating = false;
            _meditateBtn.Text = "TỌA QUAN NHẬP ĐỊNH";
            _meditateBtn.BackColor = Jade;
            _sessionDurationSeconds = 25 * 60;
            _seconds = 25 * 60;
            UpdateTimer();
            _world.Invalidate();
        };

        breakButton.Click += (_, _) =>
        {
            _pomodoro.Stop();
            _timerRunning = false;
            _isMeditating = false;
            _meditateBtn.Text = "TỌA QUAN NHẬP ĐỊNH";
            _meditateBtn.BackColor = Jade;
            _sessionDurationSeconds = 5 * 60;
            _seconds = 5 * 60;
            UpdateTimer();
            _world.Invalidate();
        };

        lectureBtn.Click += (_, _) => AskRandomMentorLecture();

        UpdateTimer();
    }

    private void BuildChatPanel(TableLayoutPanel parent)
    {
        var panel = CreateSectionPanel();
        panel.Padding = new Padding(14);
        parent.Controls.Add(panel, 2, 0);

        var heading = CreateSectionTitle("TRUYỀN ÂM LUẬN ĐẠO");
        heading.Dock = DockStyle.Top;
        heading.Height = 38;
        panel.Controls.Add(heading);

        var description = new Label
        {
            Text = "Thần thức truyền âm giữa các đạo hữu trong động phủ.",
            Dock = DockStyle.Top,
            Height = 46,
            ForeColor = Color.FromArgb(105, 96, 78),
            Font = new Font("Segoe UI", 9.5F)
        };
        panel.Controls.Add(description);

        _chat.Dock = DockStyle.Fill;
        _chat.Font = new Font("Segoe UI", 10.2F);
        _chat.BackColor = Color.FromArgb(250, 246, 232);
        _chat.ForeColor = Ink;
        _chat.BorderStyle = BorderStyle.FixedSingle;

        var composer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 112,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 10, 0, 0)
        };
        composer.RowStyles.Add(new RowStyle(SizeType.Percent, 48F));
        composer.RowStyles.Add(new RowStyle(SizeType.Percent, 52F));

        _message.Dock = DockStyle.Fill;
        _message.Font = new Font("Segoe UI", 11F);
        _message.PlaceholderText = "Gửi thần thức truyền âm...";
        _message.Margin = new Padding(0, 0, 0, 8);

        var send = MakeButton("PHÁT TRUYỀN ÂM PHÙ", Jade);
        send.Dock = DockStyle.Fill;
        composer.Controls.Add(_message, 0, 0);
        composer.Controls.Add(send, 0, 1);
        panel.Controls.Add(composer);

        panel.Controls.Add(_chat);
        _chat.BringToFront();

        send.Click += async (_, _) => await SendChatAsync();
        _message.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await SendChatAsync();
            }
        };
    }

    private void UpdateCultivationStatus()
    {
        var next = MortalCultivationLore.GetNextRealm(_currentRealm);
        _realmBadge.Text = $"CẢNH GIỚI: {_currentRealm.Name.ToUpperInvariant()} ({_currentRealm.Title})";
        _stoneLabel.Text = $"HẠ PHẨM LINH THẠCH: {_spiritStones}  |  THỜI GIAN TỌA QUAN: {_totalCultivatedMinutes} PHÚT";

        if (next == null)
        {
            _cultivationBar.Maximum = 100;
            _cultivationBar.Value = 100;
        }
        else
        {
            int span = Math.Max(1, next.RequiredFocusMinutes - _currentRealm.RequiredFocusMinutes);
            int cur = Math.Clamp(_totalCultivatedMinutes - _currentRealm.RequiredFocusMinutes, 0, span);
            _cultivationBar.Maximum = span;
            _cultivationBar.Value = cur;
        }
    }

    private void HandleWorldClick(object? sender, MouseEventArgs e)
    {
        int w = _world.ClientSize.Width;
        int h = _world.ClientSize.Height;

        // Check if user clicked on a mentor
        foreach (var mentor in MortalCultivationLore.Mentors)
        {
            int mx = (int)(w * mentor.RelX);
            int my = (int)(h * mentor.RelY);
            if (Math.Abs(e.X - mx) <= 36 && Math.Abs(e.Y - my) <= 45)
            {
                string quote = mentor.Lectures[_rng.Next(mentor.Lectures.Length)];
                TriggerSpeechBubble(mentor.Name, quote);
                AddSystemMessage($"[{mentor.Name} - {mentor.Title}]: {quote}");
                return;
            }
        }

        // Check if user clicked on a classroom object (Bồ đoàn, Hư Thiên Đỉnh, Kiếm Bia, Chưởng Thiên Bình)
        foreach (var obj in MortalCultivationLore.ClassroomObjects)
        {
            int ox = (int)(w * obj.RelX);
            int oy = (int)(h * obj.RelY);
            if (Math.Abs(e.X - ox) <= 35 && Math.Abs(e.Y - oy) <= 30)
            {
                TriggerSpeechBubble(obj.Name, obj.Description);
                if (obj.Kind == "Cushion")
                {
                    _x = ox;
                    _y = oy - 6;
                    _isMeditating = true;
                    _players[_username] = new Player
                    {
                        Username = _username,
                        X = _x,
                        Y = _y,
                        Realm = _currentRealm.Name,
                        SpiritStones = _spiritStones,
                        IsMeditating = true
                    };
                    _ = _network.SendAsync(new Packet
                    {
                        Type = "MOVE",
                        X = _x,
                        Y = _y,
                        Realm = _currentRealm.Name,
                        IsMeditating = true
                    });
                }
                else if (obj.Kind == "Stele")
                {
                    AddSystemMessage("[Thanh Nguyên Kiếm Bia]: Tầng 1-3 Luyện Khí Quyết: 'Tâm như chỉ thủy, khí quán đan điền, vạn niệm quy nhất.'");
                }
                else if (obj.Kind == "Pedestal")
                {
                    AddSystemMessage("[Chưởng Thiên Bình]: Linh bình ngưng tụ ánh trăng hóa thành Lục Dịch, giúp đệ tử tăng tốc ngộ tính!");
                }
                _world.Invalidate();
                return;
            }
        }
    }

    private void InteractWithNearestEntity()
    {
        int w = _world.ClientSize.Width;
        int h = _world.ClientSize.Height;

        foreach (var mentor in MortalCultivationLore.Mentors)
        {
            int mx = (int)(w * mentor.RelX);
            int my = (int)(h * mentor.RelY);
            if (Math.Hypot(_x - mx, _y - my) <= 85)
            {
                string quote = mentor.Lectures[_rng.Next(mentor.Lectures.Length)];
                TriggerSpeechBubble(mentor.Name, quote);
                AddSystemMessage($"[{mentor.Name}]: {quote}");
                return;
            }
        }

        AskRandomMentorLecture();
    }

    private void AskRandomMentorLecture()
    {
        var mentor = MortalCultivationLore.Mentors[_rng.Next(MortalCultivationLore.Mentors.Count)];
        string quote = mentor.Lectures[_rng.Next(mentor.Lectures.Length)];
        TriggerSpeechBubble(mentor.Name, quote);
        AddSystemMessage($"[{mentor.Name} truyền âm]: {quote}");
    }

    private void TriggerSpeechBubble(string speaker, string text)
    {
        _activeBubbleSpeaker = speaker;
        _activeBubbleText = text;
        _bubbleTicksLeft = 130; // ~6 seconds
        _world.Invalidate();
    }

    private Panel CreateSectionPanel() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = Paper,
        Margin = new Padding(8),
        BorderStyle = BorderStyle.FixedSingle
    };

    private static Label CreateSectionTitle(string text) => new()
    {
        Text = text,
        Font = new Font("Georgia", 11.5F, FontStyle.Bold),
        ForeColor = Color.FromArgb(82, 61, 38),
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Button MakeButton(string text, Color color) => new()
    {
        Text = text,
        Height = 44,
        MinimumSize = new Size(132, 44),
        AutoSize = false,
        Dock = DockStyle.Fill,
        Margin = new Padding(3, 4, 3, 4),
        Font = new Font("Segoe UI", 9.2F, FontStyle.Bold),
        ForeColor = Color.White,
        BackColor = color,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
        TextAlign = ContentAlignment.MiddleCenter
    };

    private async Task SendChatAsync()
    {
        string message = _message.Text.Trim();
        if (message.Length == 0) return;

        if (string.IsNullOrWhiteSpace(_roomTitle.Text) ||
            _roomTitle.Text.StartsWith("TRUYỀN CÔNG ĐẠI ĐIỆN"))
        {
            MessageBox.Show(this, "Vui lòng tiến vào một động phủ trước khi phát truyền âm phù.",
                "Tông Môn Nhắc Nhở", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            await _network.SendAsync(new Packet { Type = "CHAT", Text = message });
            Ui(() =>
            {
                if (string.Equals(_message.Text.Trim(), message, StringComparison.Ordinal))
                    _message.Clear();
            });
        }
        catch (Exception ex)
        {
            ShowNetworkError(ex);
        }
    }

    private void HandlePacket(Packet p) => Ui(() =>
    {
        switch (p.Type)
        {
            case "ROOM_LIST_RESULT":
                string? previouslySelected = _rooms.SelectedItem as string;
                _rooms.BeginUpdate();
                try
                {
                    _rooms.Items.Clear();
                    foreach (string chamber in (p.Text ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries))
                        if (!_rooms.Items.Cast<string>().Contains(chamber, StringComparer.OrdinalIgnoreCase))
                            _rooms.Items.Add(chamber);

                    if (previouslySelected != null)
                    {
                        for (int i = 0; i < _rooms.Items.Count; i++)
                        {
                            if (string.Equals(_rooms.Items[i]?.ToString(), previouslySelected,
                                StringComparison.OrdinalIgnoreCase))
                            {
                                _rooms.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                }
                finally { _rooms.EndUpdate(); }
                break;

            case "JOINED_ROOM":
                _roomTitle.Text = "ĐỘNG PHỦ: " + p.RoomName?.ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(p.RoomName))
                {
                    for (int i = 0; i < _rooms.Items.Count; i++)
                    {
                        if (string.Equals(_rooms.Items[i]?.ToString(), p.RoomName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            _rooms.SelectedIndex = i;
                            break;
                        }
                    }
                }
                if (p.X > 0) _x = p.X;
                if (p.Y > 0) _y = p.Y;
                _chat.Items.Clear();
                _players.Clear();
                _players[_username] = new Player
                {
                    Username = _username,
                    X = _x,
                    Y = _y,
                    Realm = _currentRealm.Name,
                    SpiritStones = _spiritStones,
                    IsMeditating = _isMeditating
                };
                _world.Invalidate();
                AddSystemMessage($"Ngươi đã bước vào động phủ [{p.RoomName}]. Linh khí xung quanh vô cùng dồi dào!");
                break;

            case "CHAT":
                string realmTag = string.IsNullOrWhiteSpace(p.Realm) ? "" : $"[{p.Realm}] ";
                AddChatMessage($"{realmTag}{p.Username ?? "Vô Danh Tu Sĩ"}", p.Text ?? "");
                if (!string.IsNullOrWhiteSpace(p.Username))
                    TriggerSpeechBubble(p.Username, p.Text ?? "");
                break;

            case "SYSTEM":
                AddSystemMessage(p.Text ?? "");
                break;

            case "PLAYER_JOINED":
            case "PLAYER_MOVED":
                if (p.Username != null)
                {
                    _players[p.Username] = new Player
                    {
                        Username = p.Username,
                        X = p.X,
                        Y = p.Y,
                        Realm = string.IsNullOrWhiteSpace(p.Realm) ? "Luyện Khí Tầng 1" : p.Realm,
                        IsMeditating = p.IsMeditating
                    };
                    _world.Invalidate();
                }
                break;

            case "CULTIVATE_STATE":
                if (p.Username != null)
                {
                    if (!_players.TryGetValue(p.Username, out var pl))
                    {
                        pl = new Player { Username = p.Username, X = p.X, Y = p.Y };
                        _players[p.Username] = pl;
                    }
                    if (!string.IsNullOrWhiteSpace(p.Realm)) pl.Realm = p.Realm;
                    pl.IsMeditating = p.IsMeditating;
                    _world.Invalidate();
                    if (!string.IsNullOrWhiteSpace(p.Text))
                        AddSystemMessage($"[{pl.Realm}] {p.Username} {p.Text}");
                }
                break;

            case "PLAYER_LEFT":
                if (p.Username != null) _players.Remove(p.Username);
                _world.Invalidate();
                break;

            case "POMODORO":
                if (p.Username != null && _players.TryGetValue(p.Username, out var medPlayer) && !string.IsNullOrWhiteSpace(p.Realm))
                {
                    medPlayer.Realm = p.Realm;
                    medPlayer.IsMeditating = false;
                    _world.Invalidate();
                }
                _chat.Items.Add($"[THIÊN ĐẠO THÔNG BÁO] {p.Username} {p.Text}");
                ScrollChatToBottom();
                break;

            case "ACTION_RESULT":
            case "ERROR":
                if (!p.Success)
                    MessageBox.Show(this, p.Text ?? "Có biến cố ngoài ý muốn xảy ra.",
                        "Tông Môn Nhắc Nhở", MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
        }
    });

    private void AddChatMessage(string senderDisplay, string message)
    {
        _chat.Items.Add($"{senderDisplay}: {message}");
        ScrollChatToBottom();
    }

    private void AddSystemMessage(string message)
    {
        _chat.Items.Add($"[TÔNG MÔN] {message}");
        ScrollChatToBottom();
    }

    private void ScrollChatToBottom()
    {
        if (_chat.Items.Count > 0) _chat.TopIndex = _chat.Items.Count - 1;
    }

    private void DrawWorld(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int w = _world.ClientSize.Width;
        int h = _world.ClientSize.Height;
        if (w <= 10 || h <= 10) return;

        // 1. Bầu trời tiên cảnh Hoàng Phong Cốc
        using (var sky = new LinearGradientBrush(
            new Rectangle(0, 0, w, h),
            Color.FromArgb(172, 204, 188),
            Color.FromArgb(236, 224, 188), 90F))
        {
            g.FillRectangle(sky, 0, 0, w, h);
        }

        // Vầng linh nguyệt / Thái Dương trên đỉnh núi
        int sunX = (int)(w * 0.80f);
        int sunY = (int)(h * 0.13f);
        using (var sunGlow = new SolidBrush(Color.FromArgb(65, 255, 240, 185)))
            g.FillEllipse(sunGlow, sunX - 38, sunY - 38, 76, 76);
        using (var sunCore = new SolidBrush(Color.FromArgb(230, 255, 248, 215)))
            g.FillEllipse(sunCore, sunX - 22, sunY - 22, 44, 44);

        // 2. Dãy núi Thiên Nam & sương mù linh khí
        using var mountain1 = new SolidBrush(Color.FromArgb(116, 138, 115));
        using var mountain2 = new SolidBrush(Color.FromArgb(142, 156, 124));
        Point[] m1 = { new(0, h * 44 / 100), new(w * 17 / 100, h * 11 / 100), new(w * 37 / 100, h * 44 / 100) };
        Point[] m2 = { new(w * 63 / 100, h * 44 / 100), new(w * 84 / 100, h * 9 / 100), new(w, h * 44 / 100) };
        g.FillPolygon(mountain1, m1);
        g.FillPolygon(mountain2, m2);

        // 3. Sân đá bạch ngọc của Truyền Công Giảng Đường
        int floorTop = (int)(h * 0.43f);
        using (var floorBrush = new LinearGradientBrush(
            new Rectangle(0, floorTop, w, h - floorTop),
            Color.FromArgb(208, 196, 162),
            Color.FromArgb(184, 170, 134), 90F))
        {
            g.FillRectangle(floorBrush, 0, floorTop, w, h - floorTop);
        }

        // Lưới gạch đá cổ trên sân giảng đường
        using (var tilePen = new Pen(Color.FromArgb(45, 110, 95, 65), 1))
        {
            for (int ty = floorTop + 28; ty < h; ty += 36)
                g.DrawLine(tilePen, 0, ty, w, ty);
            for (int tx = 40; tx < w; tx += 64)
                g.DrawLine(tilePen, tx, floorTop, tx, h);
        }

        // 4. Đại Điện Hoàng Phong Cốc (Truyền Công Điện)
        int hallW = Math.Min((int)(w * 0.66f), 620);
        int hallX = (w - hallW) / 2;
        int hallY = (int)(h * 0.12f);
        int hallH = (int)(h * 0.28f);

        using var wall = new SolidBrush(Color.FromArgb(112, 68, 44));
        using var roof = new SolidBrush(Color.FromArgb(44, 70, 58));
        using var trim = new Pen(Color.FromArgb(224, 184, 102), 3.5f);

        g.FillRectangle(wall, hallX + 26, hallY + 42, hallW - 52, hallH);
        Point[] roofPoints =
        {
            new(hallX - 12, hallY + 48),
            new(hallX + hallW / 2, hallY - 14),
            new(hallX + hallW + 12, hallY + 48),
            new(hallX + hallW - 14, hallY + 56),
            new(hallX + hallW / 2, hallY + 10),
            new(hallX + 14, hallY + 56)
        };
        g.FillPolygon(roof, roofPoints);
        g.DrawPolygon(trim, roofPoints);
        g.DrawRectangle(trim, hallX + 26, hallY + 42, hallW - 52, hallH);

        // Hoành phi "TRUYỀN CÔNG ĐIỆN"
        int plaqueW = 230;
        int plaqueH = 30;
        int plaqueX = hallX + (hallW - plaqueW) / 2;
        int plaqueY = hallY + 16;
        using (var plaqueBg = new SolidBrush(Color.FromArgb(56, 36, 24)))
        using (var plaqueBorder = new Pen(Color.FromArgb(232, 192, 108), 2))
        {
            g.FillRectangle(plaqueBg, plaqueX, plaqueY, plaqueW, plaqueH);
            g.DrawRectangle(plaqueBorder, plaqueX, plaqueY, plaqueW, plaqueH);
        }
        using (var signFont = new Font("Georgia", 10.5F, FontStyle.Bold))
        using (var signBrush = new SolidBrush(Color.FromArgb(250, 222, 148)))
        {
            const string sign = "TRUYỀN CÔNG ĐIỆN";
            var sz = g.MeasureString(sign, signFont);
            g.DrawString(sign, signFont, signBrush, plaqueX + (plaqueW - sz.Width) / 2, plaqueY + 6);
        }

        // Đài Chưởng tọa & Bát Quái Tụ Linh Trận giữa giảng đường
        int arrayCenterX = (int)(w * 0.50f);
        int arrayCenterY = (int)(h * 0.66f);
        int arrayRx = (int)(w * 0.25f);
        int arrayRy = (int)(h * 0.18f);
        float pulse = (float)(Math.Sin(_animTick) * 0.5 + 0.5);

        using (var arrayPenOuter = new Pen(Color.FromArgb(95 + (int)(pulse * 55), 55, 145, 115), 2.5f))
        using (var arrayPenInner = new Pen(Color.FromArgb(85, 205, 165, 75), 1.8f))
        {
            g.DrawEllipse(arrayPenOuter, arrayCenterX - arrayRx, arrayCenterY - arrayRy, arrayRx * 2, arrayRy * 2);
            g.DrawEllipse(arrayPenInner, arrayCenterX - arrayRx * 0.72f, arrayCenterY - arrayRy * 0.72f, arrayRx * 1.44f, arrayRy * 1.44f);
        }

        // Cổ thụ linh mộc hai bên
        DrawTree(g, (int)(w * 0.08f), (int)(h * 0.48f));
        DrawTree(g, (int)(w * 0.92f), (int)(h * 0.48f));

        // 5. Vẽ các pháp bảo & vật phẩm giảng đường (Bồ đoàn, Hư Thiên Đỉnh, Thanh Nguyên Kiếm Bia, Chưởng Thiên Bình)
        foreach (var obj in MortalCultivationLore.ClassroomObjects)
        {
            int ox = (int)(w * obj.RelX);
            int oy = (int)(h * obj.RelY);
            DrawSpiritualObject(g, ox, oy, obj);
        }

        // 6. Vẽ Tam Đại Truyền Công Trưởng Lão (Hàn Lập, Nam Cung Uyển, Tử Linh)
        foreach (var mentor in MortalCultivationLore.Mentors)
        {
            int mx = (int)(w * mentor.RelX);
            int my = (int)(h * mentor.RelY);
            DrawCultivator(g, mx, my, mentor.Name, mentor.Realm, mentor.RobeColor, mentor.AuraColor, true, false);
        }

        // 7. Vẽ toàn bộ đệ tử trong động phủ (sắp xếp theo trục Y để tạo chiều sâu)
        foreach (var player in _players.Values.OrderBy(p => p.Y))
        {
            var realmInfo = MortalCultivationLore.Realms.FirstOrDefault(r => r.Name == player.Realm)
                            ?? MortalCultivationLore.Realms[0];
            DrawCultivator(g, player.X, player.Y, player.Username, player.Realm,
                realmInfo.RobeColor, realmInfo.AuraColor, false, player.IsMeditating);
        }

        // 8. Hạt linh khí bay lơ lửng
        foreach (var pt in _particles)
        {
            using var pBrush = new SolidBrush(Color.FromArgb(pt.Alpha, pt.Tint));
            g.FillEllipse(pBrush, pt.X, pt.Y, pt.Size, pt.Size);
        }

        // 9. Thoại truyền âm nổi trên giảng đường
        if (!string.IsNullOrWhiteSpace(_activeBubbleText))
        {
            DrawSpeechBanner(g, w, _activeBubbleSpeaker, _activeBubbleText);
        }
    }

    private void DrawSpiritualObject(Graphics g, int x, int y, SpiritualObject obj)
    {
        if (obj.Kind == "Cushion")
        {
            using var shadow = new SolidBrush(Color.FromArgb(60, 40, 30, 20));
            using var cushion = new SolidBrush(Color.FromArgb(148, 92, 48));
            using var rim = new Pen(Color.FromArgb(228, 192, 112), 2);
            g.FillEllipse(shadow, x - 22, y + 6, 44, 14);
            g.FillEllipse(cushion, x - 20, y, 40, 16);
            g.DrawEllipse(rim, x - 20, y, 40, 16);
        }
        else if (obj.Kind == "Cauldron")
        {
            // Hư Thiên Đỉnh
            using var body = new SolidBrush(Color.FromArgb(68, 76, 86));
            using var goldTrim = new Pen(Color.FromArgb(225, 185, 95), 2);
            using var mist = new SolidBrush(Color.FromArgb(95, 175, 130, 245));
            g.FillEllipse(body, x - 18, y - 12, 36, 28);
            g.DrawEllipse(goldTrim, x - 18, y - 12, 36, 28);
            g.FillEllipse(mist, x - 14, y - 28 - (float)Math.Sin(_animTick) * 3, 28, 16);
            DrawSmallLabel(g, x, y + 20, "Hư Thiên Đỉnh");
        }
        else if (obj.Kind == "Stele")
        {
            // Thanh Nguyên Kiếm Bia
            using var stone = new SolidBrush(Color.FromArgb(86, 98, 92));
            using var rune = new Pen(Color.FromArgb(120, 245, 195), 1.8f);
            g.FillRectangle(stone, x - 14, y - 28, 28, 44);
            g.DrawRectangle(rune, x - 11, y - 24, 22, 36);
            g.DrawLine(rune, x, y - 20, x, y + 8);
            DrawSmallLabel(g, x, y + 20, "Kiếm Bia");
        }
        else if (obj.Kind == "Pedestal")
        {
            // Chưởng Thiên Bình Đài
            using var baseBrush = new SolidBrush(Color.FromArgb(115, 108, 92));
            using var bottleBrush = new SolidBrush(Color.FromArgb(45, 155, 112));
            using var glow = new SolidBrush(Color.FromArgb(110, 120, 255, 185));
            g.FillEllipse(glow, x - 22, y - 24, 44, 34);
            g.FillRectangle(baseBrush, x - 14, y - 2, 28, 14);
            g.FillEllipse(bottleBrush, x - 7, y - 16, 14, 16);
            DrawSmallLabel(g, x, y + 15, "Chưởng Thiên Bình");
        }
    }

    private static void DrawSmallLabel(Graphics g, int x, int y, string text)
    {
        using var f = new Font("Segoe UI", 7.5F, FontStyle.Bold);
        var sz = g.MeasureString(text, f);
        using var bg = new SolidBrush(Color.FromArgb(165, 35, 34, 28));
        g.FillRectangle(bg, x - sz.Width / 2 - 4, y, sz.Width + 8, 15);
        g.DrawString(text, f, Brushes.LightGoldenrodYellow, x - sz.Width / 2, y + 1);
    }

    private static void DrawTree(Graphics g, int x, int y)
    {
        using var trunk = new SolidBrush(Color.FromArgb(92, 62, 38));
        using var leaves = new SolidBrush(Color.FromArgb(48, 86, 62));
        using var blossom = new SolidBrush(Color.FromArgb(228, 198, 118));
        g.FillRectangle(trunk, x - 6, y, 12, 60);
        g.FillEllipse(leaves, x - 38, y - 48, 76, 72);
        g.FillEllipse(leaves, x - 26, y - 72, 52, 56);
        g.FillEllipse(blossom, x - 14, y - 35, 7, 7);
        g.FillEllipse(blossom, x + 12, y - 48, 6, 6);
    }

    private void DrawCultivator(Graphics g, int x, int y, string name, string realm, Color robeColor, Color auraColor, bool mentor, bool meditating)
    {
        int bodyW = mentor ? 28 : 23;
        int bodyH = mentor ? 42 : (meditating ? 28 : 35);

        // Vòng hào quang linh khí khi đang nhập định hoặc là Trưởng lão
        if (meditating || mentor)
        {
            float pulse = (float)(Math.Sin(_animTick * 1.4f) * 4);
            int auraAlpha = meditating ? 95 : 55;
            using var auraBrush = new SolidBrush(Color.FromArgb(auraAlpha, auraColor));
            using var auraPen = new Pen(Color.FromArgb(175, auraColor), 1.8f);
            g.FillEllipse(auraBrush, x - 26 - pulse / 2, y + bodyH - 12 - pulse / 2, 52 + pulse, 22 + pulse / 2);
            g.DrawEllipse(auraPen, x - 26 - pulse / 2, y + bodyH - 12 - pulse / 2, 52 + pulse, 22 + pulse / 2);
        }

        using var shadow = new SolidBrush(Color.FromArgb(65, 38, 30, 22));
        using var robe = new SolidBrush(robeColor);
        using var skin = new SolidBrush(Color.FromArgb(236, 204, 168));
        using var hair = new SolidBrush(Color.FromArgb(36, 32, 29));
        using var outline = new Pen(mentor ? Color.FromArgb(245, 212, 125) : Color.FromArgb(232, 222, 195), 1.8f);

        g.FillEllipse(shadow, x - 20, y + bodyH - 2, 40, 10);

        if (meditating)
        {
            // Tư thế khoanh chân tọa quan
            Point[] seatedRobe =
            {
                new(x - bodyW / 2, y + 10),
                new(x + bodyW / 2, y + 10),
                new(x + bodyW / 2 + 12, y + bodyH),
                new(x - bodyW / 2 - 12, y + bodyH)
            };
            g.FillPolygon(robe, seatedRobe);
            g.DrawPolygon(outline, seatedRobe);
        }
        else
        {
            Point[] clothes =
            {
                new(x - bodyW / 2, y + 11),
                new(x + bodyW / 2, y + 11),
                new(x + bodyW / 2 + 7, y + bodyH),
                new(x - bodyW / 2 - 7, y + bodyH)
            };
            g.FillPolygon(robe, clothes);
            g.DrawPolygon(outline, clothes);
        }

        // Đầu & búi tóc đạo sĩ
        g.FillEllipse(skin, x - 8, y - 5, 16, 17);
        g.FillEllipse(hair, x - 9, y - 9, 18, 11);
        g.FillEllipse(hair, x - 4, y - 14, 8, 7); // Đạo kế (búi tóc)
        g.FillRectangle(hair, x - 10, y - 1, 3, 18);
        g.FillRectangle(hair, x + 7, y - 1, 3, 18);

        // Bảng tên & Cảnh giới
        using var nameFont = new Font("Segoe UI", mentor ? 8.3F : 8F, FontStyle.Bold);
        using var realmFont = new Font("Segoe UI", 7F, FontStyle.Regular);

        string displayName = name.Length > 20 ? name[..20] : name;
        string displayRealm = meditating ? $"⚡ {realm} (Đang Tọa Quan)" : realm;

        var nameSize = g.MeasureString(displayName, nameFont);
        var realmSize = g.MeasureString(displayRealm, realmFont);
        float boxW = Math.Max(nameSize.Width, realmSize.Width) + 12;
        float boxH = 31;

        var labelRect = new RectangleF(x - boxW / 2, y + bodyH + 4, boxW, boxH);
        using var back = new SolidBrush(mentor ? Color.FromArgb(215, 58, 42, 26) : Color.FromArgb(200, 32, 42, 38));
        using var border = new Pen(mentor ? Color.FromArgb(235, 195, 105) : auraColor, 1.2f);
        using (var labelPath = RoundedRectangle(labelRect, 5))
        {
            g.FillPath(back, labelPath);
            g.DrawPath(border, labelPath);
        }

        g.DrawString(displayName, nameFont, mentor ? Brushes.Gold : Brushes.White, x - nameSize.Width / 2, y + bodyH + 6);
        using var realmBrush = new SolidBrush(auraColor);
        g.DrawString(displayRealm, realmFont, realmBrush, x - realmSize.Width / 2, y + bodyH + 19);
    }

    private static void DrawSpeechBanner(Graphics g, int canvasWidth, string speaker, string text)
    {
        int bannerW = Math.Min(canvasWidth - 40, 660);
        int bannerX = (canvasWidth - bannerW) / 2;
        int bannerY = 12;
        var rect = new RectangleF(bannerX, bannerY, bannerW, 54);

        using var bg = new SolidBrush(Color.FromArgb(228, 34, 44, 38));
        using var border = new Pen(Color.FromArgb(232, 192, 104), 2);
        using var path = RoundedRectangle(rect, 8);
        g.FillPath(bg, path);
        g.DrawPath(border, path);

        using var spkFont = new Font("Segoe UI", 8.8F, FontStyle.Bold);
        using var txtFont = new Font("Georgia", 9.5F, FontStyle.Italic);
        g.DrawString($"✦ {speaker}:", spkFont, Brushes.Gold, bannerX + 14, bannerY + 7);
        g.DrawString(text, txtFont, Brushes.Ivory, new RectangleF(bannerX + 14, bannerY + 25, bannerW - 28, 24));
    }

    private static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void UpdateTimer() =>
        _timerLabel.Text = $"{_seconds / 60:00}:{_seconds % 60:00}";

    private void ShowNetworkError(Exception ex) =>
        MessageBox.Show(this, ex.Message, "Linh Trận Trục Trặc", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        if (!IsHandleCreated)
        {
            _ = Handle;
        }
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }
}
