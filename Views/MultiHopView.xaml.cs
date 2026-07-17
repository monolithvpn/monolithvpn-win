using System.Windows;
using System.Windows.Controls;
using MonolithVpnClient.Models;
using MonolithVpnClient.Services;

namespace MonolithVpnClient.Views;

public partial class MultiHopView : UserControl
{
    private readonly ApiClient _api;
    private List<ServerInfo> _servers = new();

    public MultiHopView(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        Loaded += async (_, _) => await LoadServersAsync();
        ConnectionState.Changed += Render;
        Unloaded += (_, _) => ConnectionState.Changed -= Render;
        Render();
    }

    private async Task LoadServersAsync()
    {
        StatusText.Text = "Loading servers...";
        try
        {
            _servers = await _api.GetServersAsync();
        }
        catch (Exception)
        {
            StatusText.Text = "Couldn't load the server list - check your connection and try again.";
            return;
        }

        var selectable = _servers.Where(s => !s.Maintenance).OrderBy(s => s.Name).ToList();
        EntryCombo.ItemsSource = selectable;
        ExitCombo.ItemsSource = selectable;

        if (ConnectionManager.IsMultiHop)
        {
            EntryCombo.SelectedItem = selectable.FirstOrDefault(s => s.Id == ConnectionManager.MultiHopEntryServerId);
            ExitCombo.SelectedItem = selectable.FirstOrDefault(s => s.Id == ConnectionManager.ConnectedServerId);
        }
        else if (selectable.Count >= 2)
        {
            EntryCombo.SelectedIndex = 0;
            ExitCombo.SelectedIndex = 1;
        }

        Render();
    }

    private void Render()
    {
        bool connectedHere = ConnectionManager.IsMultiHop;
        ConnectButton.Content = connectedHere ? "Disconnect" : "Connect";
        EntryCombo.IsEnabled = !connectedHere;
        ExitCombo.IsEnabled = !connectedHere;

        if (connectedHere)
        {
            var entry = _servers.FirstOrDefault(s => s.Id == ConnectionManager.MultiHopEntryServerId);
            var exit = _servers.FirstOrDefault(s => s.Id == ConnectionManager.ConnectedServerId);
            StatusText.Text = $"Connected via {entry?.Name ?? "entry"} → {exit?.Name ?? "exit"}.";
        }
        else if (ConnectionManager.ConnectedServerId is not null)
        {
            StatusText.Text = "You're already connected to a single server - disconnect it first, or just connect here to switch to multi-hop.";
        }
        else if (StatusText.Text != "Loading servers..." && !StatusText.Text.StartsWith("Couldn't"))
        {
            StatusText.Text = "Not connected.";
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionManager.IsMultiHop)
        {
            ConnectButton.IsEnabled = false;
            StatusText.Text = "Disconnecting...";
            var exit = _servers.FirstOrDefault(s => s.Id == ConnectionManager.ConnectedServerId);
            var (success, error) = await ConnectionManager.DisconnectAsync(
                ConnectionManager.ConnectedServerId ?? 0, exit?.Name ?? "multi-hop");
            ConnectButton.IsEnabled = true;
            if (!success)
            {
                ValidationText.Text = error ?? "Couldn't disconnect - try again.";
                ValidationText.Visibility = Visibility.Visible;
            }
            Render();
            return;
        }

        var entryServer = EntryCombo.SelectedItem as ServerInfo;
        var exitServer = ExitCombo.SelectedItem as ServerInfo;
        ValidationText.Visibility = Visibility.Collapsed;

        if (entryServer is null || exitServer is null)
        {
            ValidationText.Text = "Pick both an entry and an exit server.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }
        if (entryServer.Id == exitServer.Id)
        {
            ValidationText.Text = "Entry and exit servers must be different.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        ConnectButton.IsEnabled = false;
        StatusText.Text = $"Connecting via {entryServer.Name} → {exitServer.Name}...";
        try
        {
            var (success, error, _) = await ConnectionManager.ConnectMultiHopAsync(_api, entryServer, exitServer);
            if (!success)
            {
                ValidationText.Text = error ?? "Couldn't connect - try again.";
                ValidationText.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            ConnectButton.IsEnabled = true;
            Render();
        }
    }
}
