using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MonolithVpnClient.Models;
using MonolithVpnClient.Services;

namespace MonolithVpnClient.Views;

public partial class SimpleView : UserControl
{
    private readonly ApiClient _api;
    private readonly ObservableCollection<ServerRow> _rows = new();
    private readonly DispatcherTimer _refreshTimer;
    private bool _pinging;

    public SimpleView(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        ServerCombo.ItemsSource = _rows;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _refreshTimer.Tick += async (_, _) =>
        {
            await LoadServersAsync(preserveSelection: true);
            _ = RefreshRowPingsAsync();
            _ = RefreshPingAsync();
        };

        ConnectionState.Changed += RefreshDisplay;
        RefreshDisplay();
    }

    public void Activate()
    {
        _ = ActivateAsync();
    }

    private async Task ActivateAsync()
    {
        await LoadServersAsync(preserveSelection: true);
        _refreshTimer.Start();
        _ = RefreshRowPingsAsync();
        _ = RefreshPingAsync();
    }

    public void Deactivate() => _refreshTimer.Stop();

    private async Task RefreshRowPingsAsync()
    {
        var rows = _rows.ToList();
        var tasks = rows.Select(async row =>
        {
            var ms = await PingService.PingMsAsync(row.Info.Hostname);
            row.PingMs = ms ?? -1;
        });
        await Task.WhenAll(tasks);
    }

    private async Task LoadServersAsync(bool preserveSelection)
    {
        int? previousSelectedId = preserveSelection && ServerCombo.SelectedItem is ServerRow prevRow ? prevRow.Info.Id : null;

        List<ServerInfo> servers;
        try
        {
            servers = await _api.GetServersAsync();
        }
        catch
        {
            return;
        }

        _rows.Clear();
        foreach (var s in servers.OrderBy(s => s.Name))
            _rows.Add(new ServerRow(s) { IsConnected = s.Id == ConnectionManager.ConnectedServerId });

        ServerRow? toSelect = ConnectionManager.ConnectedServerId is int connectedId
            ? _rows.FirstOrDefault(r => r.Info.Id == connectedId)
            : null;
        toSelect ??= previousSelectedId is int prevId ? _rows.FirstOrDefault(r => r.Info.Id == prevId) : null;
        toSelect ??= _rows.FirstOrDefault();

        ServerCombo.SelectedItem = toSelect;
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ConnectButton.IsEnabled = _rows.Count > 0;
        RefreshDisplay();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (ServerCombo.SelectedItem is not ServerRow selected) return;

        if (TunnelTransition.InProgress)
        {
            ShowError("Still finishing a previous connection change - try again in a moment.");
            return;
        }

        bool connectedHere = selected.Info.Id == ConnectionManager.ConnectedServerId;

        if (!connectedHere && ConnectionManager.ConnectedServerId is int currentId && currentId != selected.Info.Id)
        {
            string currentName = ConnectionState.Current?.Name ?? "your current server";
            bool proceed = ConfirmDialogService.ShowYesNo(
                "Switch servers?",
                $"You're connected to {currentName}. Disconnect and connect to {selected.Info.Name} instead?",
                confirmText: "Switch", cancelText: "Cancel");
            if (!proceed) return;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        ConnectButton.IsEnabled = false;
        TunnelTransition.InProgress = true;
        try
        {
            if (connectedHere)
            {
                var (success, error) = await ConnectionManager.DisconnectAsync(selected.Info.Id, selected.Info.Name);
                if (!success) ShowError(error);
            }
            else
            {
                var (success, error, _) = await ConnectionManager.ConnectAsync(_api, selected.Info);
                if (!success) ShowError(error);
            }
        }
        finally
        {
            ConnectButton.IsEnabled = true;
            TunnelTransition.InProgress = false;
            RefreshDisplay();
        }
    }

    private void ShowError(string? error)
    {
        ErrorText.Text = error ?? "Something went wrong.";
        ErrorText.Visibility = Visibility.Visible;
    }

    private void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshDisplay();

    private void RefreshDisplay()
    {
        var current = ConnectionState.Current;
        bool connected = current is not null;

        StatusDot.Fill = (System.Windows.Media.Brush)FindResource(!connected ? "TextLow" : current!.Online == false ? "Red" : "Green");
        StatusText.Text = !connected ? "Not connected" : current!.Online == false ? "Server offline" : $"Connected to {current.Name}";

        ServerInfoPanel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        if (connected) LoadBar.Width = Math.Max(0, Math.Min(100, current!.LoadPct)) * 1.2;

        if (ServerCombo.SelectedItem is ServerRow selected)
        {
            bool connectedHere = connected && selected.Info.Id == ConnectionManager.ConnectedServerId;
            ConnectButton.Content = connectedHere ? "Disconnect" : "Connect";
        }
    }

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
}
