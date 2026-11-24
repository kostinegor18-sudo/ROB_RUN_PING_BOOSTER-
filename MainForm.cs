using System.Diagnostics;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RobRunPingBooster;

public sealed class MainForm : Form
{
    private readonly Label _ping = Metric("—");
    private readonly Label _jitter = Metric("—");
    private readonly Label _loss = Metric("—");
    private readonly Label _status = new();
    private readonly Label _network = new();
    private readonly TextBox _host = new() { Text = "1.1.1.1" };
    private readonly Button _monitorButton = new();
    private readonly Button _optimizeButton = new();
    private readonly PingGraph _graph = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly List<long> _samples = new();
    private int _sent, _received;
    private long? _previous;
    private bool _busy;

    private static readonly Color Bg = Color.FromArgb(10, 15, 12);
    private static readonly Color Panel = Color.FromArgb(17, 26, 20);
    private static readonly Color Green = Color.FromArgb(65, 226, 119);
    private static readonly Color Muted = Color.FromArgb(142, 162, 148);

    public MainForm()
    {
        Text = "ROB RUN PING BOOSTER";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(850, 650);
        Size = new Size(1060, 760);
        BackColor = Bg;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);
        BuildUi();
        _timer.Tick += async (_, _) => await ProbeAsync();
        Shown += async (_, _) => { await RefreshNetworkInfoAsync(); };
    }

    private static Label Metric(string value) => new()
    {
        Text = value, AutoSize = true, ForeColor = Green,
        Font = new Font("Segoe UI", 25, FontStyle.Bold)
    };

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(24),
            ColumnCount = 1, RowCount = 5, BackColor = Bg
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 125));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        var title = new Label
        {
            Text = "ROB RUN PING BOOSTER", AutoSize = true,
            Font = new Font("Segoe UI", 21, FontStyle.Bold),
            ForeColor = Color.FromArgb(238, 255, 242), Location = new Point(0, 2)
        };
        var subtitle = new Label
        {
            Text = "СЕТЕВАЯ ДИАГНОСТИКА И МОНИТОРИНГ",
            AutoSize = true, ForeColor = Muted, Location = new Point(2, 43)
        };
        header.Controls.Add(title); header.Controls.Add(subtitle);
        root.Controls.Add(header, 0, 0);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = Bg, Padding = new Padding(0, 4, 0, 4)
        };
        for (int i = 0; i < 3; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        metrics.Controls.Add(MetricCard("PING", _ping), 0, 0);
        metrics.Controls.Add(MetricCard("JITTER", _jitter), 1, 0);
        metrics.Controls.Add(MetricCard("PACKET LOSS", _loss), 2, 0);
        root.Controls.Add(metrics, 0, 1);

        _graph.Dock = DockStyle.Fill;
        _graph.BackColor = Panel;
        root.Controls.Add(_graph, 0, 2);

        var controls = new Panel { Dock = DockStyle.Fill, BackColor = Panel, Padding = new Padding(14) };
        var hostLabel = new Label { Text = "Узел для проверки (IP или домен)", ForeColor = Muted, AutoSize = true, Location = new Point(14, 12) };
        _host.SetBounds(14, 39, 220, 36);
        StyleTextBox(_host);
        _monitorButton.Text = "Запустить мониторинг";
        StyleButton(_monitorButton, false);
        _monitorButton.SetBounds(248, 38, 205, 38);
        _monitorButton.Click += (_, _) => ToggleMonitor();
        _optimizeButton.Text = "Проверить и подобрать настройки";
        StyleButton(_optimizeButton, true);
        _optimizeButton.SetBounds(465, 38, 260, 38);
        _optimizeButton.Click += async (_, _) => await RunDiagnosticsAsync();
        _network.SetBounds(14, 82, 800, 25);
        _network.ForeColor = Muted;
        _network.AutoEllipsis = true;
        controls.Controls.AddRange([hostLabel, _host, _monitorButton, _optimizeButton, _network]);
        root.Controls.Add(controls, 0, 3);

        _status.Text = "Готово. Укажите адрес для измерения задержки.";
        _status.ForeColor = Muted;
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(_status, 0, 4);
    }

    private static Control MetricCard(string caption, Label value)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Panel, Margin = new Padding(0, 0, 10, 0), Padding = new Padding(15) };
        var label = new Label { Text = caption, ForeColor = Muted, AutoSize = true, Location = new Point(15, 12) };
        value.Location = new Point(15, 39);
        panel.Controls.Add(label); panel.Controls.Add(value);
        return panel;
    }

    private static void StyleButton(Button b, bool primary)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = primary ? Green : Color.FromArgb(49, 91, 61);
        b.BackColor = primary ? Green : Color.FromArgb(24, 59, 37);
        b.ForeColor = primary ? Color.FromArgb(5, 18, 9) : Color.White;
        b.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        b.Cursor = Cursors.Hand;
    }

    private static void StyleTextBox(TextBox box)
    {
        box.BackColor = Bg; box.ForeColor = Color.White;
        box.BorderStyle = BorderStyle.FixedSingle;
    }

    private void ToggleMonitor()
    {
        if (_timer.Enabled)
        {
            _timer.Stop();
            _monitorButton.Text = "Запустить мониторинг";
            _status.Text = "Мониторинг остановлен.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_host.Text))
        {
            MessageBox.Show("Введите IP-адрес или доменное имя.", "ROB RUN PING BOOSTER");
            return;
        }
        _sent = _received = 0;
        _previous = null;
        _samples.Clear();
        _graph.SetSamples(_samples);
        _monitorButton.Text = "Остановить мониторинг";
        _timer.Start();
        _ = ProbeAsync();
    }

    private async Task ProbeAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(_host.Text.Trim(), 1200);
            _sent++;
            if (reply.Status == IPStatus.Success)
            {
                _received++;
                long ms = reply.RoundtripTime;
                long jitter = _previous.HasValue ? Math.Abs(ms - _previous.Value) : 0;
                _previous = ms;
                _samples.Add(ms);
                if (_samples.Count > 60) _samples.RemoveAt(0);
                _ping.Text = $"{ms} ms";
                _jitter.Text = $"{jitter} ms";
                _status.Text = $"Ответ получен от {reply.Address}";
            }
            else
            {
                _sent++;
                _samples.Add(-1);
                _ping.Text = "TIMEOUT";
                _status.Text = $"Нет ответа: {reply.Status}";
            }
            _loss.Text = $"{100.0 * (_sent - _received) / Math.Max(1, _sent):0.0}%";
            _graph.SetSamples(_samples);
        }
        catch (Exception ex)
        {
            _status.Text = $"Ошибка проверки: {ex.Message}";
        }
        finally { _busy = false; }
    }

    private async Task RefreshNetworkInfoAsync()
    {
        await Task.Run(() =>
        {
            var active = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => $"{n.Name} ({n.NetworkInterfaceType})")
                .ToArray();
            BeginInvoke(() => _network.Text = active.Length == 0
                ? "Сетевой адаптер не найден."
                : "Активные адаптеры: " + string.Join(" · ", active));
        });
    }

    private async Task RunDiagnosticsAsync()
    {
        _optimizeButton.Enabled = false;
        _status.Text = "Проверка сетевого соединения…";
        try
        {
            var target = _host.Text.Trim();
            if (target.Length == 0) target = "1.1.1.1";
            var result = await Task.Run(async () =>
            {
                var rows = new List<string>();
                foreach (var host in new[] { target, "1.1.1.1", "8.8.8.8" }.Distinct())
                {
                    try
                    {
                        using var p = new Ping();
                        var r = await p.SendPingAsync(host, 1600);
                        rows.Add(r.Status == IPStatus.Success
                            ? $"{host}: {r.RoundtripTime} мс"
                            : $"{host}: {r.Status}");
                    }
                    catch (Exception e) { rows.Add($"{host}: {e.Message}"); }
                }
                return string.Join(Environment.NewLine, rows);
            });

            MessageBox.Show(
                "Результаты базовой диагностики:" + Environment.NewLine + result +
                Environment.NewLine + Environment.NewLine +
                "Программа пока не меняет системные TCP/DNS-параметры: универсальные «твики» не гарантируют снижение игрового пинга и могут ухудшить соединение. " +
                "Для оптимизации нужно сначала определить фактический IP игрового сервера ROB RUN.",
                "ROB RUN PING BOOSTER — диагностика",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            _status.Text = "Диагностика завершена. Системные настройки не изменялись.";
        }
        finally { _optimizeButton.Enabled = true; }
    }
}

public sealed class PingGraph : Control
{
    private IReadOnlyList<long> _samples = Array.Empty<long>();
    public PingGraph()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }
    public void SetSamples(IReadOnlyList<long> samples)
    {
        _samples = samples.ToArray();
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var gridPen = new Pen(Color.FromArgb(43, 61, 49), 1);
        using var textBrush = new SolidBrush(Color.FromArgb(142, 162, 148));
        using var linePen = new Pen(Color.FromArgb(65, 226, 119), 2.5f);
        var area = RectangleF.Inflate(ClientRectangle, -22, -22);
        for (int i = 0; i < 4; i++)
        {
            float y = area.Top + area.Height * i / 3f;
            g.DrawLine(gridPen, area.Left, y, area.Right, y);
        }
        g.DrawString("ИСТОРИЯ ЗАДЕРЖКИ · последние 60 измерений", Font, textBrush, 18, 8);
        if (_samples.Count < 2)
        {
            g.DrawString("Запустите мониторинг, чтобы увидеть график", Font, textBrush, 22, area.Top + 35);
            return;
        }
        float max = Math.Max(100, _samples.Where(x => x > 0).DefaultIfEmpty(100).Max() + 20);
        max = Math.Min(max, 2000);
        PointF? previous = null;
        for (int i = 0; i < _samples.Count; i++)
        {
            long value = _samples[i];
            if (value < 0) { previous = null; continue; }
            float x = area.Left + area.Width * i / Math.Max(1, _samples.Count - 1);
            float y = area.Bottom - area.Height * Math.Min(value, max) / max;
            var point = new PointF(x, y);
            if (previous.HasValue) g.DrawLine(linePen, previous.Value, point);
            previous = point;
        }
    }
}
