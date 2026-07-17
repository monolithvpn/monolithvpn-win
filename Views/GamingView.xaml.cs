using System.Windows;
using System.Windows.Controls;
using MonolithVpnClient.Models;
using MonolithVpnClient.Services;

namespace MonolithVpnClient.Views;

public partial class GamingView : UserControl
{
    private readonly ApiClient _api;
    private List<GameInfo> _games = new();

    public GamingView(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        LowLatencyCheckBox.IsChecked = AppSettings.Current.SplitTunnelEnabled;
        CustomRangesBox.Text = string.Join(Environment.NewLine, AppSettings.Current.SplitTunnelCustomRanges);
        RenderAppsList();
        Loaded += async (_, _) => await LoadGamesAsync();
    }

    private async Task LoadGamesAsync()
    {
        GamesLoadingText.Visibility = Visibility.Visible;
        GamesErrorText.Visibility = Visibility.Collapsed;
        GamesEmptyText.Visibility = Visibility.Collapsed;
        GamesList.Items.Clear();

        try
        {
            _games = await _api.GetGamesAsync();
        }
        catch (Exception)
        {
            GamesLoadingText.Visibility = Visibility.Collapsed;
            GamesErrorText.Text = "Couldn't load the games list - check your connection and try again.";
            GamesErrorText.Visibility = Visibility.Visible;
            return;
        }

        GamesLoadingText.Visibility = Visibility.Collapsed;
        if (_games.Count == 0)
        {
            GamesEmptyText.Visibility = Visibility.Visible;
            RecomputeEffectiveRanges();
            return;
        }

        var validIds = AppSettings.Current.SplitTunnelSelectedGameIds.Intersect(_games.Select(g => g.Id)).ToList();
        AppSettings.Current.SplitTunnelSelectedGameIds = validIds;

        foreach (var game in _games.OrderBy(g => g.Name))
        {
            int udpPortCount = PortRedirectService.ParseUdpRanges(game.Ports).Count;
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = game.Name, FontSize = 13 });
            label.Children.Add(new TextBlock
            {
                Text = DescribeGameCoverage(game.CidrRanges.Count, udpPortCount),
                FontSize = 11,
                Foreground = (System.Windows.Media.Brush)FindResource("TextLow"),
            });

            var checkBox = new CheckBox
            {
                Content = label,
                Style = (Style)FindResource("AppCheckBox"),
                Margin = new Thickness(0, 6, 0, 6),
                Tag = game.Id,
                IsChecked = validIds.Contains(game.Id),
            };
            checkBox.Checked += GameCheckBox_Changed;
            checkBox.Unchecked += GameCheckBox_Changed;
            GamesList.Items.Add(checkBox);
        }

        RecomputeEffectiveRanges();
    }

    private static string DescribeGameCoverage(int rangeCount, int udpPortCount)
    {
        if (rangeCount > 0 && udpPortCount > 0) return $"{rangeCount} IP range(s), {udpPortCount} UDP port range(s)";
        if (rangeCount > 0) return $"{rangeCount} IP range(s)";
        if (udpPortCount > 0) return $"{udpPortCount} UDP port range(s) - needs the bundled WinDivert driver";
        return "No usable ranges yet";
    }

    private void GameCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: int gameId } checkBox) return;
        var ids = AppSettings.Current.SplitTunnelSelectedGameIds;
        bool isChecked = checkBox.IsChecked == true;
        if (isChecked && !ids.Contains(gameId)) ids.Add(gameId);
        else if (!isChecked) ids.Remove(gameId);
        RecomputeEffectiveRanges();
    }

    private void LowLatencyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.SplitTunnelEnabled = LowLatencyCheckBox.IsChecked == true;
        AppSettings.Save();
    }

    private void SaveCustomRangesButton_Click(object sender, RoutedEventArgs e)
    {
        var lines = CustomRangesBox.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var normalized = new List<string>();
        var invalid = new List<string>();
        foreach (var line in lines)
        {
            if (SplitTunnelService.TryNormalize(line, out string result)) normalized.Add(result);
            else invalid.Add(line);
        }

        if (invalid.Count > 0)
        {
            CustomRangesStatusText.Foreground = (System.Windows.Media.Brush)FindResource("Red");
            CustomRangesStatusText.Text = $"Couldn't understand: {string.Join(", ", invalid)}. " +
                "Use a plain IPv4 address (203.0.113.4) or CIDR range (203.0.113.0/24) - nothing was saved.";
            CustomRangesStatusText.Visibility = Visibility.Visible;
            return;
        }

        AppSettings.Current.SplitTunnelCustomRanges = normalized.Distinct().ToList();
        CustomRangesBox.Text = string.Join(Environment.NewLine, AppSettings.Current.SplitTunnelCustomRanges);
        RecomputeEffectiveRanges();

        CustomRangesStatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextLow");
        CustomRangesStatusText.Text = "Saved. Takes effect the next time you connect.";
        CustomRangesStatusText.Visibility = Visibility.Visible;
    }

    private void RecomputeEffectiveRanges()
    {
        var settings = AppSettings.Current;
        var selected = _games.Where(g => settings.SplitTunnelSelectedGameIds.Contains(g.Id)).ToList();
        settings.SplitTunnelExcludedRanges = selected.SelectMany(g => g.CidrRanges)
            .Concat(settings.SplitTunnelCustomRanges).Distinct().ToList();
        settings.SplitTunnelPorts = selected.SelectMany(g => g.Ports).Distinct().ToList();
        AppSettings.Save();
    }

    private void AddAppButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Pick an application",
            Filter = "Applications (*.exe)|*.exe",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;
        AddAppPath(dialog.FileName);
    }

    private void PickRunningAppButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ProcessPickerWindow { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() == true && picker.SelectedPath is string path)
            AddAppPath(path);
    }

    private void AddAppPath(string path)
    {
        var paths = AppSettings.Current.SplitTunnelAppPaths;
        if (!paths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
            paths.Add(path);
        AppSettings.Save();
        RenderAppsList();
    }

    private void RemoveAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path }) return;
        AppSettings.Current.SplitTunnelAppPaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        AppSettings.Save();
        RenderAppsList();
    }

    private void RenderAppsList()
    {
        AppsList.Items.Clear();
        var paths = AppSettings.Current.SplitTunnelAppPaths;
        AppsEmptyText.Visibility = paths.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var path in paths)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var removeButton = new Button
            {
                Content = "Remove",
                Style = (Style)FindResource("OutlineButton"),
                Height = 26,
                Padding = new Thickness(10, 0, 10, 0),
                Tag = path,
            };
            removeButton.Click += RemoveAppButton_Click;
            DockPanel.SetDock(removeButton, Dock.Right);
            row.Children.Add(removeButton);

            row.Children.Add(new TextBlock
            {
                Text = System.IO.Path.GetFileName(path),
                ToolTip = path,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            });
            AppsList.Items.Add(row);
        }
    }
}
