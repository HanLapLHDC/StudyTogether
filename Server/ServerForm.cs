using Server.Helpers;

namespace Server;

public sealed class ServerForm : Form
{
    private readonly NetworkServer _server = new();
    private readonly TextBox _log = new();
    private readonly NumericUpDown _port = new();
    private readonly Label _clients = new();
    private readonly Button _start = new();
    private readonly Button _stop = new();

    public ServerForm()
    {
        Text = "Cultivation Academy - Server Control Panel";
        Width = 820;
        Height = 580;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(239, 231, 207);

        var title = new Label
        {
            Text = "CULTIVATION ACADEMY SERVER",
            Font = new Font("Georgia", 19F, FontStyle.Bold),
            ForeColor = Color.FromArgb(48, 78, 64),
            AutoSize = true,
            Left = 24,
            Top = 20
        };
        var portLabel = new Label { Text = "TCP PORT:", Left = 28, Top = 91, AutoSize = true };
        _port.SetBounds(120, 84, 110, 36);
        _port.Minimum = 1024;
        _port.Maximum = 65535;
        _port.Value = 9000;

        _start.Text = "START SERVER";
        _start.SetBounds(255, 82, 145, 42);
        _stop.Text = "STOP SERVER";
        _stop.SetBounds(415, 82, 145, 42);
        _stop.Enabled = false;

        _clients.Text = "DISCIPLES ONLINE: 0";
        _clients.SetBounds(580, 91, 205, 28);
        _clients.AutoSize = true;

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Font = new Font("Consolas", 10.5F);
        _log.SetBounds(25, 150, 755, 350);
        _log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        Controls.AddRange([title, portLabel, _port, _start, _stop, _clients, _log]);

        _start.Click += async (_, _) =>
        {
            int port = (int)_port.Value;
            _start.Enabled = false;
            _stop.Enabled = true;
            _port.Enabled = false;
            try { await _server.StartAsync(port); }
            catch (Exception ex) { AddLog("ERROR: " + ex.Message); }
            finally
            {
                _start.Enabled = true;
                _stop.Enabled = false;
                _port.Enabled = true;
            }
        };

        _stop.Click += (_, _) =>
        {
            _server.Stop();
            _stop.Enabled = false;
        };

        _server.Log = AddLog;
        _server.ClientCountChanged = count => Ui(() => _clients.Text = $"DISCIPLES ONLINE: {count}");
        FormClosing += (_, _) => _server.Stop();
        AddLog("Ready. Select START SERVER to open the academy network.");
    }

    private void AddLog(string message) => Ui(() =>
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}"));

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }
}
