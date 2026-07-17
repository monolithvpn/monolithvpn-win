using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using MonolithVpnClient.Services;

namespace MonolithVpnClient.Views;

public partial class NetworkView : UserControl
{
    private readonly ApiClient _api;
    private readonly DispatcherTimer _trafficTimer;
    private readonly List<double> _rxSamples = new();
    private readonly List<double> _txSamples = new();
    private const int MaxSamples = 60;

    private long? _lastRxBytes;
    private long? _lastTxBytes;
    private DateTime _lastSampleAt;
    private Polyline? _rxLine;
    private Polyline? _txLine;
    private Polygon? _rxFill;
    private Polygon? _txFill;
    private bool _trafficPaused = true;
    private int _tickCount;

    public NetworkView(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        Render();

        _trafficTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _trafficTimer.Tick += (_, _) =>
        {
            if (!_trafficPaused) SampleTraffic();
            _tickCount++;
            if (_tickCount % 2 == 0) _ = RefreshPingAsync();
        };

        Loaded += (_, _) =>
        {
            ConnectionState.Changed += Render;
            _trafficTimer.Start();
            Render();
        };
        Unloaded += (_, _) =>
        {
            ConnectionState.Changed -= Render;
            _trafficTimer.Stop();
        };
    }

    private bool _pinging;

    private async Task RefreshPingAsync()
    {
        if (_pinging) return;
        var current = ConnectionState.Current;
        if (current is null) { PingText.Text = "-- ms"; return; }

        _pinging = true;
        try
        {
            var ms = await PingService.PingMsAsync(current.Hostname);
            PingText.Text = ms is int value ? $"{value} ms" : "timeout";
        }
        finally
        {
            _pinging = false;
        }
    }

    private void EnsureLines()
    {
        if (_rxLine is not null) return;
        var green = (System.Windows.Media.Brush)Application.Current.Resources["Green"];
        var red = (System.Windows.Media.Brush)Application.Current.Resources["Red"];

        _rxFill = new Polygon { Fill = green, Opacity = 0.12 };
        _txFill = new Polygon { Fill = red, Opacity = 0.12 };
        _rxLine = new Polyline
        {
            Stroke = green, StrokeThickness = 1.75,
            StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
        };
        _txLine = new Polyline
        {
            Stroke = red, StrokeThickness = 1.75,
            StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
        };
        TrafficCanvas.Children.Add(_rxFill);
        TrafficCanvas.Children.Add(_txFill);
        TrafficCanvas.Children.Add(_rxLine);
        TrafficCanvas.Children.Add(_txLine);
    }

    private void SampleTraffic()
    {
        var current = ConnectionState.Current;
        if (current is null)
        {
            _lastRxBytes = null;
            _lastTxBytes = null;
            _rxSamples.Clear();
            _txSamples.Clear();
            TrafficCanvas.Children.Clear();
            _rxLine = null;
            _txLine = null;
            _rxFill = null;
            _txFill = null;
            DownloadSpeedText.Text = "0 Kbps";
            UploadSpeedText.Text = "0 Kbps";
            return;
        }

        string tunnelName = WireGuardService.TunnelNameFor(current.Id);
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.Name.Equals(tunnelName, StringComparison.OrdinalIgnoreCase));
        if (nic is null) return;

        IPv4InterfaceStatistics stats;
        try
        {
            stats = nic.GetIPv4Statistics();
        }
        catch
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (_lastRxBytes is long lastRx && _lastTxBytes is long lastTx)
        {
            double elapsed = (now - _lastSampleAt).TotalSeconds;
            if (elapsed > 0)
            {
                double rxPerSec = Math.Max(0, (stats.BytesReceived - lastRx) / elapsed);
                double txPerSec = Math.Max(0, (stats.BytesSent - lastTx) / elapsed);
                AddSample(_rxSamples, rxPerSec);
                AddSample(_txSamples, txPerSec);
                DownloadSpeedText.Text = FormatSpeed(rxPerSec);
                UploadSpeedText.Text = FormatSpeed(txPerSec);
                EnsureLines();
                Redraw();
            }
        }

        _lastRxBytes = stats.BytesReceived;
        _lastTxBytes = stats.BytesSent;
        _lastSampleAt = now;
    }

    private static void AddSample(List<double> samples, double value)
    {
        samples.Add(value);
        while (samples.Count > MaxSamples) samples.RemoveAt(0);
    }

    private void Redraw()
    {
        if (_rxLine is null || _txLine is null || _rxFill is null || _txFill is null) return;

        double width = TrafficCanvas.ActualWidth;
        double height = TrafficCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        double max = Math.Max(1, Math.Max(_rxSamples.DefaultIfEmpty(0).Max(), _txSamples.DefaultIfEmpty(0).Max()));
        var rxPoints = BuildPoints(_rxSamples, width, height, max);
        var txPoints = BuildPoints(_txSamples, width, height, max);
        _rxLine.Points = rxPoints;
        _txLine.Points = txPoints;
        _rxFill.Points = BuildFillPoints(rxPoints, height);
        _txFill.Points = BuildFillPoints(txPoints, height);
    }

    private static System.Windows.Media.PointCollection BuildPoints(List<double> samples, double width, double height, double max)
    {
        var points = new System.Windows.Media.PointCollection();
        if (samples.Count < 2) return points;

        double step = width / (MaxSamples - 1);
        double startX = width - (samples.Count - 1) * step;
        for (int i = 0; i < samples.Count; i++)
        {
            double x = startX + i * step;
            double y = height - (samples[i] / max) * (height - 4) - 2;
            points.Add(new System.Windows.Point(x, y));
        }
        return points;
    }

    private static System.Windows.Media.PointCollection BuildFillPoints(System.Windows.Media.PointCollection linePoints, double height)
    {
        var fill = new System.Windows.Media.PointCollection();
        if (linePoints.Count < 2) return fill;

        foreach (var p in linePoints) fill.Add(p);
        fill.Add(new System.Windows.Point(linePoints[^1].X, height));
        fill.Add(new System.Windows.Point(linePoints[0].X, height));
        return fill;
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        double bits = bytesPerSec * 8;
        if (bits >= 1_000_000) return $"{bits / 1_000_000:0.0} Mbps";
        if (bits >= 1_000) return $"{bits / 1_000:0} Kbps";
        return $"{bits:0} bps";
    }

    private void Render()
    {
        Dispatcher.Invoke(() =>
        {
            var current = ConnectionState.Current;
            bool connected = current is not null;

            ServerNameText.Text = connected ? current!.Name : "Not connected";
            AttackBadge.Visibility = connected && current!.UnderAttack ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = !connected
                ? "Connect from the Servers tab to see live status here."
                : current!.Online switch
                {
                    true => "Connected - tunnel is up",
                    false => "Connected, but the server looks offline right now",
                    null => "Status unknown",
                };
            LoadBar.Width = connected ? Math.Max(0, Math.Min(100, current!.LoadPct)) * 2.4 : 0;

            TrafficPauseButton.IsEnabled = connected;
            if (!connected && !_trafficPaused) StopCapturing();
        });
    }

    private void StopCapturing()
    {
        _trafficPaused = true;
        TrafficPauseButton.Content = "Capture";
        _rxSamples.Clear();
        _txSamples.Clear();
        TrafficCanvas.Children.Clear();
        _rxLine = null;
        _txLine = null;
        _rxFill = null;
        _txFill = null;
        DownloadSpeedText.Text = "Not capturing";
        UploadSpeedText.Text = "Not capturing";
    }

    private async void CheckIpButton_Click(object sender, RoutedEventArgs e)
    {
        CheckIpButton.IsEnabled = false;
        IpErrorText.Visibility = Visibility.Collapsed;
        IpResultPanel.Visibility = Visibility.Collapsed;
        CopyIpButton.Visibility = Visibility.Collapsed;
        ClearIpButton.Visibility = Visibility.Collapsed;
        try
        {
            var result = await _api.GetMyIpAsync();
            if (result.Ip is null)
            {
                IpErrorText.Text = "Couldn't determine your IPv4 address (this check only reports IPv4, not IPv6).";
                IpErrorText.Visibility = Visibility.Visible;
                return;
            }
            IpText.Text = result.Ip;
            var location = string.Join(", ", new[] { result.City, result.Country }.Where(s => !string.IsNullOrWhiteSpace(s)));
            IpLocationText.Text = string.IsNullOrEmpty(location) ? "Location unknown" : location;
            IpResultPanel.Visibility = Visibility.Visible;
            CopyIpButton.Visibility = Visibility.Visible;
            ClearIpButton.Visibility = Visibility.Visible;
        }
        catch (ApiException ex)
        {
            IpErrorText.Text = ex.Message;
            IpErrorText.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            IpErrorText.Text = "Couldn't reach the server to check your IP.";
            IpErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            CheckIpButton.IsEnabled = true;
        }
    }

    private void CopyIpButton_Click(object sender, RoutedEventArgs e)
    {
        var text = string.IsNullOrEmpty(IpLocationText.Text) ? IpText.Text : $"{IpText.Text} ({IpLocationText.Text})";
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch
        {
        }
    }

    private void ClearIpButton_Click(object sender, RoutedEventArgs e)
    {
        IpResultPanel.Visibility = Visibility.Collapsed;
        IpErrorText.Visibility = Visibility.Collapsed;
        CopyIpButton.Visibility = Visibility.Collapsed;
        ClearIpButton.Visibility = Visibility.Collapsed;
    }

    private async void CheckNatButton_Click(object sender, RoutedEventArgs e)
    {
        CheckNatButton.IsEnabled = false;
        NatErrorText.Visibility = Visibility.Collapsed;
        NatResultPanel.Visibility = Visibility.Collapsed;
        ClearNatButton.Visibility = Visibility.Collapsed;

        bool viaVpn = ConnectionState.Current is not null;
        try
        {
            var result = await NatTypeService.CheckAsync();
            var (badgeStyle, badgeForeground, label) = result.Type switch
            {
                NatType.Open => ("StatusBadgeGreen", "Green", "Open"),
                NatType.Moderate => ("StatusBadgeAmber", "Amber", "Moderate"),
                NatType.Strict => ("StatusBadgeRed", "Red", "Strict"),
                _ => ("StatusBadgeGray", "TextLow", "Unknown"),
            };
            NatBadge.Style = (Style)FindResource(badgeStyle);
            NatBadgeText.Foreground = (System.Windows.Media.Brush)FindResource(badgeForeground);
            NatBadgeText.Text = label;

            string path = viaVpn ? "through your VPN tunnel" : "on your regular connection (not connected to a VPN)";
            NatDetailText.Text = result.ExternalIp is not null
                ? $"Checked {path} - external {result.ExternalIp}:{result.ExternalPort}."
                : $"Checked {path} - no response from either STUN server (blocked or no connectivity).";
            NatResultPanel.Visibility = Visibility.Visible;
            ClearNatButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            NatErrorText.Text = $"Couldn't check NAT type: {ex.Message}";
            NatErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            CheckNatButton.IsEnabled = true;
        }
    }

    private void ClearNatButton_Click(object sender, RoutedEventArgs e)
    {
        NatResultPanel.Visibility = Visibility.Collapsed;
        NatErrorText.Visibility = Visibility.Collapsed;
        ClearNatButton.Visibility = Visibility.Collapsed;
    }

    private void TrafficPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_trafficPaused && ConnectionState.Current is null) return;

        if (!_trafficPaused)
        {
            StopCapturing();
            return;
        }

        _trafficPaused = false;
        TrafficPauseButton.Content = "Stop capturing";
        _lastRxBytes = null;
        _lastTxBytes = null;
    }
}
