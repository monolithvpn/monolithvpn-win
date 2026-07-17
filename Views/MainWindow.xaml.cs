using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MonolithVpnClient.Models;
using MonolithVpnClient.Services;

namespace MonolithVpnClient.Views;

public partial class MainWindow : Window
{
    private readonly ApiClient _api;
    private readonly TrayIconService _tray;
    private readonly DispatcherTimer _pingTimer;
    private readonly DispatcherTimer _updateCheckTimer;
    private bool _collapsed;
    private bool _pinging;

    private string? _lastPromptedVersion;

    private ServersView? _serversView;
    private NetworkView? _networkView;
    private SharingView? _sharingView;
    private GamingView? _gamingView;
    private MultiHopView? _multiHopView;
    private AlertsView? _alertsView;
    private HistoryView? _historyView;
    private ChangelogView? _changelogView;
    private ProfileView? _profileView;
    private HelpView? _helpView;
    private SettingsView? _settingsView;
    private SimpleView? _simpleView;

    public MainWindow(ApiClient api, string username)
    {
        InitializeComponent();
        _api = api;
        UsernameText.Text = username;
        ShowView("servers");
        SetLayoutMode(AppSettings.Current.SimpleLayoutEnabled);
        _tray = new TrayIconService(this);
        Loaded += async (_, _) => await CheckForUpdateAsync();

        _updateCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _updateCheckTimer.Tick += async (_, _) => await CheckForUpdateAsync();
        _updateCheckTimer.Start();

        ConnectionState.Changed += RenderConnectionStatus;
        RenderConnectionStatus();
        _pingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pingTimer.Tick += async (_, _) =>
        {
            if (ConnectionState.IsRecovering) RenderConnectionStatus();
            await RefreshPingAsync();
        };
        _pingTimer.Start();
        Closed += (_, _) => ConnectionState.Changed -= RenderConnectionStatus;
    }

    private void RenderConnectionStatus()
    {
        var current = ConnectionState.Current;
        if (current is null)
        {
            ConnectionStatusDot.Fill = (System.Windows.Media.Brush)FindResource("TextLow");
            ConnectionStatusText.Text = "Not connected";
            ConnectionPingText.Text = "";
            SidebarFlagBorder.Visibility = Visibility.Collapsed;
            QuickConnectButtonLabel.Text = "Quick Connect";
        }
        else if (ConnectionState.IsRecovering)
        {
            ConnectionStatusDot.Fill = (System.Windows.Media.Brush)FindResource("Amber");
            var remaining = ConnectionState.NextRetryAt - DateTime.UtcNow;
            ConnectionStatusText.Text = remaining is { } r && r.TotalSeconds > 0
                ? $"Reconnecting - retrying in {Math.Ceiling(r.TotalSeconds)}s"
                : "Reconnecting...";
            QuickConnectButtonLabel.Text = "Disconnect";
            SetSidebarFlag(current.FlagUrl);
        }
        else
        {
            ConnectionStatusDot.Fill = (System.Windows.Media.Brush)FindResource(current.Online == false ? "Red" : "Green");
            ConnectionStatusText.Text = current.Online == false ? "Server offline" : current.Name;
            QuickConnectButtonLabel.Text = "Disconnect";
            SetSidebarFlag(current.FlagUrl);
        }
    }

    private void SetSidebarFlag(string? flagUrl)
    {
        if (string.IsNullOrWhiteSpace(flagUrl))
        {
            SidebarFlagBorder.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            SidebarFlagBorder.Background = new ImageBrush(new BitmapImage(new Uri(flagUrl))) { Stretch = Stretch.UniformToFill };
            SidebarFlagBorder.Visibility = Visibility.Visible;
        }
        catch
        {
            SidebarFlagBorder.Visibility = Visibility.Collapsed;
        }
    }

    private async Task RefreshPingAsync()
    {
        if (_pinging) return;
        var current = ConnectionState.Current;
        if (current is null)
        {
            ConnectionPingText.Text = "";
            return;
        }

        _pinging = true;
        try
        {
            var ms = await PingService.PingMsAsync(current.Hostname);
            ConnectionPingText.Text = ms is int value ? $"· {value} ms" : "· timeout";
        }
        finally
        {
            _pinging = false;
        }
    }

    private bool _checkingForUpdate;

    private async Task CheckForUpdateAsync()
    {
        if (_checkingForUpdate) return;
        _checkingForUpdate = true;
        try
        {
            await CheckForUpdateCoreAsync();
        }
        finally
        {
            _checkingForUpdate = false;
        }
    }

    private async Task CheckForUpdateCoreAsync()
    {
        var result = await UpdateService.CheckAsync(_api);
        if (!result.UpdateAvailable || result.LatestVersion is null) return;
        if (result.LatestVersion == _lastPromptedVersion) return;
        _lastPromptedVersion = result.LatestVersion;

        bool update = ConfirmDialogService.ShowYesNo(
            "Update available",
            $"Version {result.LatestVersion} is available - you're on {result.CurrentVersion}. "
                + "Would you like to update now? The app will close and reopen automatically - "
                + "Windows may ask you to approve the install.",
            confirmText: "Update now", cancelText: "Not now");

        if (!update || result.DownloadUrl is null) return;

        ToastService.Show("Updating", "Downloading the update...", ToastKind.Info);
        var updateResult = await UpdateService.PerformAutoUpdateAsync(_api, result.DownloadUrl);
        if (!updateResult.Success)
        {
            ToastService.Show(
                "Update failed",
                updateResult.Error ?? "Couldn't install the update - try again from Settings.",
                ToastKind.Error);
            return;
        }
        Application.Current.Shutdown();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _collapsed = !_collapsed;
        SidebarColumn.Width = new GridLength(_collapsed ? 84 : 200);

        var buttonPadding = _collapsed ? new Thickness(4, 0, 4, 0) : new Thickness(12, 0, 12, 0);
        QuickConnectButton.Padding = buttonPadding;
        LogoutButton.Padding = buttonPadding;

        var labelVisibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        NavServersLabel.Visibility = labelVisibility;
        NavNetworkLabel.Visibility = labelVisibility;
        NavSharingLabel.Visibility = labelVisibility;
        NavGamingLabel.Visibility = labelVisibility;
        NavAlertsLabel.Visibility = labelVisibility;
        NavHistoryLabel.Visibility = labelVisibility;
        NavChangelogLabel.Visibility = labelVisibility;
        NavProfileLabel.Visibility = labelVisibility;
        NavHelpLabel.Visibility = labelVisibility;
        NavSettingsLabel.Visibility = labelVisibility;

        NavSectionVpn.Visibility = labelVisibility;
        NavSectionActivity.Visibility = labelVisibility;
        NavSectionAccount.Visibility = labelVisibility;

        SidebarBrandText.Visibility = labelVisibility;
        UsernameText.Visibility = labelVisibility;
        LogoutButtonLabel.Visibility = labelVisibility;
        ConnectionStatusText.Visibility = labelVisibility;
        ConnectionPingText.Visibility = labelVisibility;
        QuickConnectButtonLabel.Visibility = labelVisibility;
    }

    private async void QuickConnectButton_Click(object sender, RoutedEventArgs e)
    {
        QuickConnectButton.IsEnabled = false;
        try
        {
            if (ConnectionManager.ConnectedServerId is int connectedId)
            {
                var name = ConnectionState.Current?.Name ?? "the server";
                var (success, error) = await ConnectionManager.DisconnectAsync(connectedId, name);
                if (!success && error is not null)
                    ToastService.Show("Disconnect failed", error, ToastKind.Error);
            }
            else
            {
                await QuickConnectAsync();
            }
        }
        finally
        {
            QuickConnectButton.IsEnabled = true;
        }
    }

    private async Task QuickConnectAsync()
    {
        List<ServerInfo> servers;
        try
        {
            servers = await _api.GetServersAsync();
        }
        catch (ApiException ex)
        {
            ToastService.Show("Quick Connect failed", ex.Message, ToastKind.Error);
            return;
        }
        catch (Exception)
        {
            ToastService.Show("Quick Connect failed", "Couldn't reach the server to load your server list.", ToastKind.Error);
            return;
        }

        var online = servers.Where(s => s.Online != false).ToList();
        if (online.Count == 0)
        {
            ToastService.Show("Quick Connect failed", "No servers are available right now.", ToastKind.Error);
            return;
        }

        ServerInfo target;
        if (online.Count == 1)
        {
            target = online[0];
        }
        else
        {
            var pinged = await Task.WhenAll(online.Select(async s => (Server: s, Ping: await PingService.PingMsAsync(s.Hostname))));
            var reachable = pinged.Where(p => p.Ping is not null).ToList();
            target = reachable.Count > 0 ? reachable.OrderBy(p => p.Ping!.Value).First().Server : online[0];
        }

        var (success, error, _) = await ConnectionManager.ConnectAsync(_api, target);
        if (!success) ToastService.Show("Quick Connect failed", error ?? "Couldn't connect.", ToastKind.Error);
    }

    private const double DefaultWindowWidth = 960, DefaultWindowHeight = 640;
    private const double DefaultWindowMinWidth = 760, DefaultWindowMinHeight = 480;
    private const double SimpleWindowWidth = 420, SimpleWindowHeight = 560;
    private const double SimpleWindowMinWidth = 380, SimpleWindowMinHeight = 420;

    private void DefaultLayoutButton_Click(object sender, RoutedEventArgs e) => SetLayoutMode(false);

    private void SimpleLayoutButton_Click(object sender, RoutedEventArgs e) => SetLayoutMode(true);

    private void SetLayoutMode(bool simple)
    {
        AppSettings.Current.SimpleLayoutEnabled = simple;
        AppSettings.Save();

        if (simple)
        {
            _simpleView ??= new SimpleView(_api);
            SimpleLayoutHost.Content = _simpleView;
            SimpleLayoutHost.Visibility = Visibility.Visible;
            DefaultLayoutGrid.Visibility = Visibility.Collapsed;
            _simpleView.Activate();
        }
        else
        {
            _simpleView?.Deactivate();
            SimpleLayoutHost.Visibility = Visibility.Collapsed;
            DefaultLayoutGrid.Visibility = Visibility.Visible;
        }

        ResizeWindowForLayout(simple);

        var activeBrush = (System.Windows.Media.Brush)FindResource("Red");
        var inactiveBrush = (System.Windows.Media.Brush)FindResource("BorderColor");
        DefaultLayoutButton.BorderBrush = simple ? inactiveBrush : activeBrush;
        SimpleLayoutButton.BorderBrush = simple ? activeBrush : inactiveBrush;
    }

    private void ResizeWindowForLayout(bool simple)
    {
        double newWidth = simple ? SimpleWindowWidth : DefaultWindowWidth;
        double newHeight = simple ? SimpleWindowHeight : DefaultWindowHeight;
        double centerX = Left + Width / 2;
        double centerY = Top + Height / 2;

        MinWidth = simple ? SimpleWindowMinWidth : DefaultWindowMinWidth;
        MinHeight = simple ? SimpleWindowMinHeight : DefaultWindowMinHeight;
        Width = newWidth;
        Height = newHeight;

        Left = centerX - newWidth / 2;
        Top = centerY - newHeight / 2;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag }) ShowView(tag);
    }

    private void ShowView(string name)
    {
        ContentHost.Content = name switch
        {
            "servers" => _serversView ??= new ServersView(_api),
            "network" => _networkView ??= new NetworkView(_api),
            "sharing" => _sharingView ??= new SharingView(),
            "gaming" => _gamingView ??= new GamingView(_api),
            "multihop" => _multiHopView ??= new MultiHopView(_api),
            "alerts" => _alertsView ??= new AlertsView(),
            "history" => _historyView ??= new HistoryView(),
            "changelog" => _changelogView ??= new ChangelogView(_api),
            "profile" => _profileView ??= new ProfileView(_api),
            "help" => _helpView ??= new HelpView(),
            "settings" => _settingsView ??= new SettingsView(_api),
            _ => ContentHost.Content,
        };
    }

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ConnectionManager.ConnectedServerId is int serverId)
            {
                string name = ConnectionState.Current?.Name ?? "the server";
                bool confirmed = ConfirmDialogService.ShowYesNo(
                    "Log out?",
                    $"You're connected to {name}. Logging out will disconnect the tunnel.",
                    confirmText: "Log out", cancelText: "Cancel");
                if (!confirmed) return;

                var (success, disconnectError) = await ConnectionManager.DisconnectAsync(serverId, name);
                if (!success && disconnectError is not null)
                    ToastService.Show("Disconnect failed", disconnectError, ToastKind.Error);
            }

            await _api.LogoutAsync();
            TokenStorage.Clear();
            _tray.AllowRealClose();
            var login = new LoginWindow();
            Application.Current.MainWindow = login;
            login.Show();
            ToastService.Show("Logged out", "Goodbye!", ToastKind.Info);
            Close();
        }
        catch (Exception ex)
        {
            ToastService.Show("Couldn't log out", ex.Message, ToastKind.Error);
        }
    }
}
