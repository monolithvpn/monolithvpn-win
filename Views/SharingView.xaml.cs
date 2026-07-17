using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MonolithVpnClient.Services;

namespace MonolithVpnClient.Views;

public record AdapterOption(string Id, string Name, string Description, string TypeLabel, bool IsUp, bool HasInternet, bool IsVpnTunnel, string? VpnServerName = null)
{
    public string DisplayName => IsVpnTunnel
        ? $"VPN: {VpnServerName ?? "active VPN tunnel"} ({(IsUp ? "connected" : "disconnected")})"
        : $"{Name} - {(IsUp ? (HasInternet ? "has internet" : "connected, no internet") : "disconnected")} ({Description})";
}

public record AdapterRow(string Name, string Description, bool Enabled, string TypeLabel)
{
    public string EnabledText => Enabled ? "Yes" : "No";
}

public partial class SharingView : UserControl
{
    private readonly DispatcherTimer _connectedDevicesTimer;
    private bool _refreshingDevices;
    private bool _passwordVisible;
    private string? _sharingPublicAdapterId;
    private string? _sharingPrivateAdapterId;
    private int? _lastKnownConnectedServerId;
    private bool _staleSharingCleanedUp;

    public SharingView()
    {
        InitializeComponent();
        LoadHotspotSettings();
        RefreshHotspotStatus();
        _lastKnownConnectedServerId = ConnectionManager.ConnectedServerId;
        RefreshAllAdapterViews();

        _connectedDevicesTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _connectedDevicesTimer.Tick += async (_, _) =>
        {
            if (_refreshingDevices) return;
            _refreshingDevices = true;
            try { await RefreshConnectedDevicesAsync(); }
            finally { _refreshingDevices = false; }
        };
        _connectedDevicesTimer.Start();
        Unloaded += (_, _) =>
        {
            _connectedDevicesTimer.Stop();
            ConnectionState.Changed -= OnConnectionStateChanged;
        };
        Loaded += async (_, _) =>
        {
            _lastKnownConnectedServerId = ConnectionManager.ConnectedServerId;
            ConnectionState.Changed += OnConnectionStateChanged;
            await RefreshConnectedDevicesAsync();
        };
    }

    private void OnConnectionStateChanged()
    {
        int? currentId = ConnectionManager.ConnectedServerId;
        if (currentId == _lastKnownConnectedServerId) return;
        _lastKnownConnectedServerId = currentId;
        RefreshAllAdapterViews();
    }

    private void RefreshAdaptersButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshAllAdapterViews();
        if (!_staleSharingCleanedUp) SharingStatusText.Text = "Adapter list refreshed.";
    }

    private void RefreshAllAdapterViews()
    {
        LoadAdapterCombos();
        AdaptersList.ItemsSource = GetAdapterRows();
    }

    private void HelpHint_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ToolTip: string text }) return;
        ConfirmDialogService.ShowInfo("What does this do?", text);
    }

    private void LoadHotspotSettings()
    {
        var saved = AppSettings.Current;
        if (!string.IsNullOrWhiteSpace(saved.HotspotSsid))
            HotspotSsidBox.Text = saved.HotspotSsid;
        if (!string.IsNullOrWhiteSpace(saved.HotspotPassword))
            HotspotPasswordBox.Password = saved.HotspotPassword;
    }

    private void HotspotSaveButton_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.HotspotSsid = HotspotSsidBox.Text.Trim();
        AppSettings.Current.HotspotPassword = HotspotPasswordBox.Password;
        AppSettings.Save();
        HotspotStatusText.Text = "Hotspot name/password saved.";
    }

    private void HotspotShowPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        _passwordVisible = !_passwordVisible;
        if (_passwordVisible)
        {
            HotspotPasswordVisibleBox.Text = HotspotPasswordBox.Password;
            HotspotPasswordBox.Visibility = Visibility.Collapsed;
            HotspotPasswordVisibleBox.Visibility = Visibility.Visible;
            HotspotShowPasswordButton.Content = "Hide";
        }
        else
        {
            HotspotPasswordBox.Password = HotspotPasswordVisibleBox.Text;
            HotspotPasswordVisibleBox.Visibility = Visibility.Collapsed;
            HotspotPasswordBox.Visibility = Visibility.Visible;
            HotspotShowPasswordButton.Content = "Show";
        }
    }

    private void HotspotPasswordVisibleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_passwordVisible) HotspotPasswordBox.Password = HotspotPasswordVisibleBox.Text;
    }

    private async Task RefreshConnectedDevicesAsync()
    {
        if (HotspotService.IsOn() != true && !IcsService.IsSharingEnabled)
        {
            ConnectedDevicesList.ItemsSource = null;
            NoDevicesText.Visibility = Visibility.Visible;
            NoDevicesText.Text = "No connected devices detected yet.";
            return;
        }

        var devices = await HotspotService.GetConnectedDevicesAsync();
        ConnectedDevicesList.ItemsSource = devices;
        NoDevicesText.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshHotspotStatus(bool resetStatusText = true)
    {
        if (!HotspotService.IsSupported())
        {
            HotspotPanel.Visibility = Visibility.Collapsed;
            HotspotUnsupportedText.Visibility = Visibility.Visible;
            HotspotWiFiUplinkWarning.Visibility = Visibility.Collapsed;
            return;
        }

        HotspotWiFiUplinkWarning.Visibility = HotspotService.UplinkIsWiFi() ? Visibility.Visible : Visibility.Collapsed;

        bool on = HotspotService.IsOn() == true;
        HotspotToggleButton.Content = on ? "Stop hotspot" : "Start hotspot";
        HotspotSsidBox.IsEnabled = !on;
        HotspotPasswordBox.IsEnabled = !on;
        HotspotPasswordVisibleBox.IsEnabled = !on;
        HotspotShowPasswordButton.IsEnabled = !on;
        if (resetStatusText) HotspotStatusText.Text = on ? "Hotspot is running." : "";
    }

    private async void HotspotToggleButton_Click(object sender, RoutedEventArgs e)
    {
        HotspotToggleButton.IsEnabled = false;
        try
        {
            if (HotspotService.IsOn() == true)
            {
                await HotspotService.StopAsync();
                HotspotStatusText.Text = "Hotspot stopped.";
            }
            else
            {
                string ssid = HotspotSsidBox.Text.Trim();
                string password = HotspotPasswordBox.Password;
                if (string.IsNullOrWhiteSpace(ssid))
                {
                    HotspotStatusText.Text = "Enter a network name first.";
                    return;
                }
                if (password.Length < 8)
                {
                    HotspotStatusText.Text = "Password needs to be at least 8 characters.";
                    return;
                }

                HotspotStatusText.Text = "Starting hotspot...";
                var (success, error) = await HotspotService.StartAsync(ssid, password);
                HotspotStatusText.Text = success
                    ? $"Hotspot \"{ssid}\" is running - other devices can connect to it now."
                    : error ?? "Couldn't start the hotspot.";
                if (success)
                {
                    AppSettings.Current.HotspotSsid = ssid;
                    AppSettings.Current.HotspotPassword = password;
                    AppSettings.Save();
                }
            }
        }
        finally
        {
            RefreshHotspotStatus(resetStatusText: false);
            HotspotToggleButton.IsEnabled = true;
        }
    }

    private void LoadAdapterCombos()
    {
        string? previousPublicId = (PublicAdapterCombo.SelectedItem as AdapterOption)?.Id;
        string? previousPrivateId = (PrivateAdapterCombo.SelectedItem as AdapterOption)?.Id;

        var options = GetAdapterOptions();
        PublicAdapterCombo.ItemsSource = options;
        PrivateAdapterCombo.ItemsSource = options;

        PublicAdapterCombo.SelectedItem = previousPublicId is null ? null : options.FirstOrDefault(a => a.Id == previousPublicId);
        PrivateAdapterCombo.SelectedItem = previousPrivateId is null ? null : options.FirstOrDefault(a => a.Id == previousPrivateId);

        _staleSharingCleanedUp = false;

        if (IcsService.IsSharingEnabled &&
            (!options.Any(a => a.Id == _sharingPublicAdapterId) || !options.Any(a => a.Id == _sharingPrivateAdapterId)))
        {
            IcsService.Disable(_sharingPublicAdapterId, _sharingPrivateAdapterId);
            _sharingPublicAdapterId = null;
            _sharingPrivateAdapterId = null;
            SharingToggleButton.Content = "Enable Sharing";
            SharingStatusText.Text = "Sharing was turned off because the VPN tunnel disconnected - the shared " +
                "device lost internet rather than leaking your real IP. Reconnect the VPN and sharing resumes automatically.";
            _staleSharingCleanedUp = true;
        }

        if (PublicAdapterCombo.SelectedItem is null)
        {
            var suggestedPublic = options.FirstOrDefault(a => a.IsVpnTunnel && a.IsUp)
                ?? options.FirstOrDefault(a => a.HasInternet);
            if (suggestedPublic is not null) PublicAdapterCombo.SelectedItem = suggestedPublic;
        }
        if (PrivateAdapterCombo.SelectedItem is null)
        {
            var suggestedPrivate = options.FirstOrDefault(a => a.IsUp && !a.HasInternet && !a.IsVpnTunnel
                && a.Id != (PublicAdapterCombo.SelectedItem as AdapterOption)?.Id);
            if (suggestedPrivate is not null) PrivateAdapterCombo.SelectedItem = suggestedPrivate;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex TunnelNamePattern =
        new(@"^monolithvpn-(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly string[] VirtualAdapterKeywords =
    {
        "Virtual", "Hyper-V", "VMware", "VirtualBox", "Bluetooth", "WAN Miniport",
        "TAP-", "Npcap", "Wintun", "Loopback",
        "Wi-Fi Direct", "WiFi Direct", "Miracast", "Direct Adapter", "PAN (Personal Area Network)",
        "ISATAP", "Teredo", "6to4", "Tunneling Pseudo-Interface", "Kernel Debug",
    };

    private static bool IsLikelyVirtual(string description) =>
        VirtualAdapterKeywords.Any(k => description.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static List<AdapterOption> GetAdapterOptions()
    {
        int? connectedServerId = ConnectionManager.ConnectedServerId;
        string? connectedServerName = ConnectionState.Current?.Name;

        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Tunnel || TunnelNamePattern.IsMatch(n.Name))
            .Where(n => TunnelNamePattern.IsMatch(n.Name) || !IsLikelyVirtual(n.Description))
            .Select(n =>
            {
                bool isUp = n.OperationalStatus == OperationalStatus.Up;
                var tunnelMatch = TunnelNamePattern.Match(n.Name);
                bool isVpnTunnel = tunnelMatch.Success;
                string? vpnServerName = isVpnTunnel
                    && int.TryParse(tunnelMatch.Groups[1].Value, out int tunnelServerId)
                    && tunnelServerId == connectedServerId
                        ? connectedServerName
                        : null;
                bool hasInternet = isVpnTunnel && isUp;
                if (isUp && !isVpnTunnel)
                {
                    try { hasInternet = n.GetIPProperties().GatewayAddresses.Count > 0; }
                    catch { }
                }
                return new AdapterOption(n.Id, n.Name, n.Description, FriendlyType(n.NetworkInterfaceType), isUp,
                    hasInternet, isVpnTunnel, vpnServerName);
            })
            .OrderByDescending(a => a.HasInternet)
            .ThenByDescending(a => a.IsUp)
            .ThenBy(a => a.Name)
            .ToList();
    }

    private static List<AdapterRow> GetAdapterRows() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Select(n => new AdapterRow(n.Name, n.Description, n.OperationalStatus == OperationalStatus.Up, FriendlyType(n.NetworkInterfaceType)))
            .OrderBy(a => a.Name)
            .ToList();

    private static string FriendlyType(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.Ethernet3Megabit or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.GigabitEthernet => "Ethernet",
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Loopback => "Loopback",
        NetworkInterfaceType.Tunnel => "Tunnel",
        NetworkInterfaceType.Ppp => "PPP",
        _ => "Other",
    };

    private void AdapterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SharingStatusText.Text = "";
    }

    private void SharingToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (IcsService.IsSharingEnabled)
        {
            var (success, error) = IcsService.Disable(_sharingPublicAdapterId, _sharingPrivateAdapterId);
            SharingStatusText.Text = success ? "Sharing disabled." : error ?? "Couldn't disable sharing.";
            SharingToggleButton.Content = "Enable Sharing";
            return;
        }

        if (PublicAdapterCombo.SelectedItem is not AdapterOption publicAdapter ||
            PrivateAdapterCombo.SelectedItem is not AdapterOption privateAdapter)
        {
            SharingStatusText.Text = "Pick both a public and a private adapter first.";
            return;
        }
        if (publicAdapter.Id == privateAdapter.Id)
        {
            SharingStatusText.Text = "Public and private adapters need to be different.";
            return;
        }

        if (!privateAdapter.IsUp)
        {
            SharingStatusText.Text = $"\"{privateAdapter.Name}\" looks disconnected - plug in the cable to the other device first, then try again.";
            return;
        }
        if (privateAdapter.TypeLabel != "Ethernet")
        {
            SharingStatusText.Text = $"\"{privateAdapter.Name}\" is a {privateAdapter.TypeLabel} adapter, not a wired Ethernet port - " +
                "this is for bridging to a device over a physical cable. Use the Hotspot section above for Wi-Fi sharing instead.";
            return;
        }
        if (!publicAdapter.IsVpnTunnel && !publicAdapter.HasInternet)
        {
            SharingStatusText.Text = $"\"{publicAdapter.Name}\" doesn't have internet access right now, so sharing it won't give the other device internet either.";
            return;
        }

        var (enableSuccess, enableError) = IcsService.Enable(publicAdapter.Id, privateAdapter.Id, publicAdapter.IsVpnTunnel);
        if (enableSuccess)
        {
            _sharingPublicAdapterId = publicAdapter.Id;
            _sharingPrivateAdapterId = privateAdapter.Id;
            SharingToggleButton.Content = "Disable Sharing";
            SharingStatusText.Text = publicAdapter.IsVpnTunnel
                ? $"Sharing your VPN connection ({publicAdapter.VpnServerName}) to \"{privateAdapter.Name}\". " +
                  "If the VPN drops, the shared device loses internet instead of leaking your real IP - " +
                  "just reconnect the VPN and sharing resumes automatically."
                : $"Sharing \"{publicAdapter.Name}\" to \"{privateAdapter.Name}\".";
        }
        else
        {
            SharingStatusText.Text = enableError ?? "Couldn't enable sharing.";
        }
    }

    private void SharingResetButton_Click(object sender, RoutedEventArgs e)
    {
        IcsService.Disable(_sharingPublicAdapterId, _sharingPrivateAdapterId);
        _sharingPublicAdapterId = null;
        _sharingPrivateAdapterId = null;
        PublicAdapterCombo.SelectedItem = null;
        PrivateAdapterCombo.SelectedItem = null;
        SharingToggleButton.Content = "Enable Sharing";
        SharingStatusText.Text = "Sharing reset.";
    }

    private void ToggleAdaptersTableButton_Click(object sender, RoutedEventArgs e)
    {
        bool showing = AdaptersTablePanel.Visibility == Visibility.Visible;
        if (showing)
        {
            AdaptersTablePanel.Visibility = Visibility.Collapsed;
            ToggleAdaptersTableButton.Content = "Show all adapters";
        }
        else
        {
            AdaptersList.ItemsSource = GetAdapterRows();
            AdaptersTablePanel.Visibility = Visibility.Visible;
            ToggleAdaptersTableButton.Content = "Hide adapters";
        }
    }
}
