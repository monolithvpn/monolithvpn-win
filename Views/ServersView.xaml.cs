using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MonolithVpnClient.Models;
using MonolithVpnClient.Services;

namespace MonolithVpnClient.Views;

public partial class ServersView : UserControl
{
    private readonly ApiClient _api;
    private readonly ObservableCollection<ServerRow> _rows = new();
    private readonly DispatcherTimer _pollTimer;
    private readonly ServerRowComparer _comparer = new();
    private ListCollectionView? _view;
    private bool _autoConnectAttempted;
    private bool _pollTickRunning;

    public ServersView(ApiClient api)
    {
        InitializeComponent();
        _api = api;

        _view = (ListCollectionView)CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = RowPassesFilter;
        _view.CustomSort = _comparer;
        ServerList.ItemsSource = _view;
        ServerGridList.ItemsSource = _view;
        UpdateViewToggleButtons();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _pollTimer.Tick += async (_, _) =>
        {
            if (_pollTickRunning) return;
            _pollTickRunning = true;
            try
            {
                await Task.WhenAll(RefreshPingsAsync(), PollConnectedStatusAsync(), CheckFreeModeTransitionAsync());
            }
            finally
            {
                _pollTickRunning = false;
            }
        };

        Loaded += async (_, _) =>
        {
            ConnectionState.Changed += SyncAllRowsConnectedState;
            await LoadServersAsync();
        };
        Unloaded += (_, _) =>
        {
            _pollTimer.Stop();
            ConnectionState.Changed -= SyncAllRowsConnectedState;
        };
    }

    private void SyncAllRowsConnectedState()
    {
        foreach (var row in _rows)
            row.IsConnected = row.Info.Id == ConnectionManager.ConnectedServerId;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadServersAsync();

    private void ListViewButton_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.ServersGridViewEnabled = false;
        AppSettings.Save();
        UpdateViewToggleButtons();
    }

    private void GridViewButton_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.ServersGridViewEnabled = true;
        AppSettings.Save();
        UpdateViewToggleButtons();
    }

    private void UpdateViewToggleButtons()
    {
        bool grid = AppSettings.Current.ServersGridViewEnabled;
        ServerList.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
        ServerGridList.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;

        var activeBrush = (System.Windows.Media.Brush)FindResource("Red");
        var inactiveBrush = (System.Windows.Media.Brush)FindResource("BorderColor");
        ListViewButton.BorderBrush = grid ? inactiveBrush : activeBrush;
        GridViewButton.BorderBrush = grid ? activeBrush : inactiveBrush;
    }

    private bool RowPassesFilter(object obj)
    {
        if (obj is not ServerRow row) return true;
        if (OnlineOnlyCheck.IsChecked == true && !row.IsOnline) return false;
        if (ProtectedOnlyCheck.IsChecked == true && !row.Info.IsProtected) return false;
        if (FreeOnlyPanel.Visibility == Visibility.Visible && FreeOnlyCheck.IsChecked == true && !row.Info.IsFreeEligible) return false;

        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(query)) return true;
        return Matches(row.Info.Name) || Matches(row.Info.City) || Matches(row.Info.Country)
            || row.Info.Tags.Any(t => Matches(t.Name));

        bool Matches(string? value) =>
            !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateEmptyStates()
    {
        bool noServersAtAll = _rows.Count == 0;
        bool noMatches = !noServersAtAll && _view is not null && !_view.Cast<object>().Any();
        EmptyState.Visibility = noServersAtAll ? Visibility.Visible : Visibility.Collapsed;
        NoMatchesState.Visibility = noMatches ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
        UpdateEmptyStates();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        _view?.Refresh();
        UpdateEmptyStates();
    }

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortCombo.SelectedItem is not ComboBoxItem item || _view is null) return;
        _comparer.SortMode = (item.Tag as string) switch
        {
            "ping" => ServerRowComparer.Mode.Ping,
            "load" => ServerRowComparer.Mode.Load,
            _ => ServerRowComparer.Mode.Name,
        };
        _view.Refresh();
    }

    private async Task RefreshPingsAsync()
    {
        var rows = _rows.ToList();
        var tasks = rows.Select(async row =>
        {
            var ms = await PingService.PingMsAsync(row.Info.Hostname);
            row.PingMs = ms ?? -1;
        });
        await Task.WhenAll(tasks);
        _view?.Refresh();
    }

    private async Task CheckFreeModeTransitionAsync()
    {
        var status = await _api.GetFreeModeStatusAsync();
        if (status is null) return;
        if (status.Enabled && !FreeModeState.LastKnownEnabled)
        {
            ToastService.Show("Free mode", status.Message ?? "Free mode is now active.", ToastKind.Info);
        }
        FreeModeState.LastKnownEnabled = status.Enabled;

        FreeOnlyPanel.Visibility = status.Enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!status.Enabled && FreeOnlyCheck.IsChecked == true)
        {
            FreeOnlyCheck.IsChecked = false;
        }
    }

    private async Task MaybeAutoConnectAsync()
    {
        if (_autoConnectAttempted || ConnectionManager.ConnectedServerId is not null) return;
        _autoConnectAttempted = true;

        if (!AppSettings.Current.AutoConnectLastServer) return;
        var lastId = AppSettings.Current.LastServerId;
        if (lastId is null) return;

        var row = _rows.FirstOrDefault(r => r.Info.Id == lastId);
        if (row is null || row.IsConnected) return;

        await ToggleConnectionAsync(row);
    }

    private async Task LoadServersAsync()
    {
        SetRefreshing(true);
        try
        {
            var servers = await _api.GetServersAsync();
            _rows.Clear();
            foreach (var s in servers) _rows.Add(new ServerRow(s) { IsConnected = s.Id == ConnectionManager.ConnectedServerId });
            UpdateEmptyStates();
            _pollTimer.Start();
            await MaybeAutoConnectAsync();
            _ = RefreshPingsAsync();
            _ = CheckFreeModeTransitionAsync();
        }
        catch (ApiException ex)
        {
            AlertLog.Add($"Couldn't load servers: {ex.Message}", "Couldn't load servers", ToastKind.Error);
        }
        catch (Exception)
        {
            AlertLog.Add("Couldn't reach the server to load your server list.", "Couldn't load servers", ToastKind.Error);
        }
        finally
        {
            SetRefreshing(false);
        }
    }

    private void SetRefreshing(bool refreshing)
    {
        RefreshButton.IsEnabled = !refreshing;
        RefreshOverlay.Visibility = refreshing ? Visibility.Visible : Visibility.Collapsed;

        var rotation = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromSeconds(0.9),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        if (refreshing)
        {
            RefreshIconRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, rotation);
            OverlaySpinnerRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, rotation);
        }
        else
        {
            RefreshIconRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
            OverlaySpinnerRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        }
    }

    private int _tunnelDownStreak;
    private int _dnsFailStreak;
    private int _healthGeneration;
    private DateTime? _nextReconnectAttemptAt;
    private bool _recoveryAnnounced;

    private async Task PollConnectedStatusAsync()
    {
        if (ConnectionManager.ConnectedServerId is not int id) return;
        var row = _rows.FirstOrDefault(r => r.Info.Id == id);
        if (row is null) return;

        try
        {
            var status = await _api.GetServerStatusAsync(id);
            if (status is null) return;

            if (ConnectionManager.ConnectedServerId != id) return;

            bool wasUnderAttack = row.Info.UnderAttack;
            bool wasOnline = row.Info.Online != false;
            row.Info = status;
            ConnectionState.Current = status;

            if (status.UnderAttack && !wasUnderAttack)
                AlertLog.Add($"{status.Name}: possible DDoS attack detected on this server.", "Possible attack", ToastKind.Error);
            if (status.Online == false && wasOnline)
                AlertLog.Add($"{status.Name}: connection appears to be offline.", "Server offline", ToastKind.Error);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            await HandleAccessRevokedAsync(id, row);
            return;
        }
        catch
        {
        }

        await CheckTunnelHealthAsync(id, row);
    }

    private async Task HandleAccessRevokedAsync(int id, ServerRow row)
    {
        if (ConnectionManager.ConnectedServerId != id) return;

        string name = row.Info.Name;
        await ConnectionManager.DisconnectAsync(id, name);
        row.IsConnected = false;
        ClearRecoveryState();
        ConnectionHistory.Add(name, "Disconnected (access no longer available)");
        AlertLog.Add(
            $"Disconnected from {name} - your plan may have expired or access was changed. Check Pricing/your account.",
            "Access no longer available", ToastKind.Error);

        await LoadServersAsync();
    }

    private async Task CheckTunnelHealthAsync(int id, ServerRow row)
    {
        if (TunnelTransition.InProgress || KillSwitchService.IsEngaged) return;
        int generation = _healthGeneration;

        bool running = await WireGuardService.IsTunnelRunningAsync(id);
        bool dnsOk = running && await WireGuardService.IsDnsRespondingAsync();

        if (generation != _healthGeneration || ConnectionManager.ConnectedServerId != id || TunnelTransition.InProgress) return;

        if (running && dnsOk)
        {
            _tunnelDownStreak = 0;
            _dnsFailStreak = 0;
            return;
        }

        if (!running)
        {
            _dnsFailStreak = 0;
            _tunnelDownStreak++;
            if (_tunnelDownStreak < 3) return;
            _tunnelDownStreak = 0;
        }
        else
        {
            _tunnelDownStreak = 0;
            _dnsFailStreak++;
            if (_dnsFailStreak < 3) return;
            _dnsFailStreak = 0;
        }

        if (!AppSettings.Current.AutoReconnectEnabled)
        {
            ClearRecoveryState();
            MarkDisconnectedAfterDrop(id, row);
            return;
        }

        if (_nextReconnectAttemptAt is DateTime next && DateTime.UtcNow < next) return;

        if (!_recoveryAnnounced)
        {
            _recoveryAnnounced = true;
            AlertLog.Add(
                $"Lost connection to {row.Info.Name} - retrying every " +
                $"{FormatInterval(AppSettings.Current.AutoReconnectRetryIntervalSeconds)} until it's back.",
                "Connection lost", ToastKind.Error);
        }

        TunnelTransition.InProgress = true;
        _healthGeneration++;
        try
        {
            await WireGuardService.DisconnectAsync(id);
            var config = await _api.ConnectAsync(id);
            await WireGuardService.ConnectAsync(id, config, row.Info.Hostname, row.Info.ObfuscationPort);
            ConnectionHistory.Add(row.Info.Name, "Reconnected");
            AlertLog.Add($"Reconnected to {row.Info.Name} after an unexpected drop.", "Reconnected", ToastKind.Success);
            ClearRecoveryState();

            try
            {
                await HotspotService.ReassertIfNeededAsync();
                IcsService.ReassertIfNeeded();
            }
            catch
            {
            }
        }
        catch
        {
            int intervalSeconds = AppSettings.Current.AutoReconnectRetryIntervalSeconds;
            _nextReconnectAttemptAt = DateTime.UtcNow.AddSeconds(intervalSeconds);
            ConnectionState.IsRecovering = true;
            ConnectionState.NextRetryAt = _nextReconnectAttemptAt;
        }
        finally
        {
            TunnelTransition.InProgress = false;
        }
    }

    private void MarkDisconnectedAfterDrop(int id, ServerRow row)
    {
        if (ConnectionManager.ConnectedServerId != id) return;

        row.IsConnected = false;
        ConnectionManager.ResetConnectedServerId();
        ConnectionState.Current = null;
        ClearRecoveryState();
        ConnectionHistory.Add(row.Info.Name, "Disconnected (unexpected drop)");
        AlertLog.Add(
            $"Lost connection to {row.Info.Name} - the tunnel dropped unexpectedly. Reconnect from the Servers tab.",
            "Connection lost", ToastKind.Error);
    }

    private void ClearRecoveryState()
    {
        _nextReconnectAttemptAt = null;
        _recoveryAnnounced = false;
        ConnectionState.IsRecovering = false;
        ConnectionState.NextRetryAt = null;
    }

    private static string FormatInterval(int seconds) => seconds switch
    {
        < 60 => $"{seconds}s",
        < 3600 when seconds % 60 == 0 => $"{seconds / 60}m",
        < 3600 => $"{seconds / 60}m {seconds % 60}s",
        _ => $"{seconds / 3600}h",
    };

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not ServerRow row) return;
        await ToggleConnectionAsync(row);
    }

    private async Task ToggleConnectionAsync(ServerRow row)
    {
        if (TunnelTransition.InProgress)
        {
            row.ErrorMessage = "Still finishing a previous connection change - try again in a moment.";
            return;
        }

        if (!row.IsConnected && ConnectionManager.ConnectedServerId is int currentId && currentId != row.Info.Id)
        {
            string currentName = ConnectionState.Current?.Name ?? "your current server";
            bool proceed = ConfirmDialogService.ShowYesNo(
                "Switch servers?",
                $"You're connected to {currentName}. Disconnect and connect to {row.Info.Name} instead?",
                confirmText: "Switch", cancelText: "Cancel");
            if (!proceed) return;
        }

        row.ErrorMessage = null;
        row.IsBusy = true;
        TunnelTransition.InProgress = true;
        _healthGeneration++;
        _tunnelDownStreak = 0;
        _dnsFailStreak = 0;
        ClearRecoveryState();
        try
        {
            if (row.IsConnected)
            {
                var (success, error) = await ConnectionManager.DisconnectAsync(row.Info.Id, row.Info.Name);
                if (success) row.IsConnected = false;
                else row.ErrorMessage = error;
            }
            else
            {
                var (success, error, previousId) = await ConnectionManager.ConnectAsync(_api, row.Info, row.SelectedEndpointIp);
                if (success)
                {
                    if (previousId is int prevId)
                    {
                        var previousRow = _rows.FirstOrDefault(r => r.Info.Id == prevId);
                        if (previousRow is not null) previousRow.IsConnected = false;
                    }
                    row.IsConnected = true;
                }
                else
                {
                    row.ErrorMessage = error;
                }
            }
        }
        finally
        {
            row.IsBusy = false;
            TunnelTransition.InProgress = false;
        }
    }

    private sealed class ServerRowComparer : IComparer
    {
        public enum Mode { Name, Ping, Load }

        public Mode SortMode { get; set; } = Mode.Ping;

        public int Compare(object? x, object? y)
        {
            if (x is not ServerRow a || y is not ServerRow b) return 0;
            return SortMode switch
            {
                Mode.Ping => ComparePing(a, b),
                Mode.Load => a.Info.LoadPct.CompareTo(b.Info.LoadPct),
                _ => string.Compare(a.Info.Name, b.Info.Name, StringComparison.OrdinalIgnoreCase),
            };
        }

        private static int ComparePing(ServerRow a, ServerRow b) => Rank(a.PingMs).CompareTo(Rank(b.PingMs));

        private static int Rank(int? ms) => ms switch
        {
            null => int.MaxValue - 1,
            -1 => int.MaxValue,
            var v => v.Value,
        };
    }
}
