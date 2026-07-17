using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MonolithVpnClient.Services;

namespace MonolithVpnClient.Views;

public partial class SettingsView : UserControl
{
    private readonly ApiClient _api;
    private bool _loadingPreferences;

    public SettingsView(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        ServerUrlText.Text = "Server: checking...";
        VersionText.Text = $"Version {UpdateService.CurrentVersion}";
        RefreshWireGuardStatus();
        LoadPreferences();
        Loaded += async (_, _) => await CheckConnectionAsync();
        Loaded += async (_, _) => await LoadDeviceUsageAsync();
    }

    private string? _pendingDownloadUrl;

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdatePanel.Visibility = Visibility.Collapsed;
        UpdateStatusText.Visibility = Visibility.Collapsed;
        try
        {
            var result = await UpdateService.CheckAsync(_api);
            if (result.UpdateAvailable)
            {
                UpdateTitleText.Text = $"Version {result.LatestVersion} is available (you have {result.CurrentVersion}).";
                UpdateNotesText.Text = string.IsNullOrWhiteSpace(result.Notes) ? "" : result.Notes;
                _pendingDownloadUrl = result.DownloadUrl;
                UpdatePanel.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateStatusText.Text = $"You're up to date (version {result.CurrentVersion}).";
                UpdateStatusText.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private async void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDownloadUrl is null) return;

        DownloadUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "Downloading and installing the update - the app will restart automatically. "
            + "Windows may ask you to approve the install.";
        UpdateStatusText.Visibility = Visibility.Visible;

        var result = await UpdateService.PerformAutoUpdateAsync(_api, _pendingDownloadUrl);
        if (!result.Success)
        {
            UpdateStatusText.Text = result.Error is null
                ? "Couldn't install the update - try again."
                : $"Couldn't install the update: {result.Error}";
            DownloadUpdateButton.IsEnabled = true;
            return;
        }
        Application.Current.Shutdown();
    }

    private void LoadPreferences()
    {
        _loadingPreferences = true;
        var settings = AppSettings.Current;
        KillSwitchCheckBox.IsChecked = settings.KillSwitchEnabled;
        AutoReconnectCheckBox.IsChecked = settings.AutoReconnectEnabled;
        AutoConnectCheckBox.IsChecked = settings.AutoConnectLastServer;
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        ObfuscationCheckBox.IsChecked = settings.ObfuscationEnabled;

        AutoReconnectIntervalPanel.Visibility = settings.AutoReconnectEnabled ? Visibility.Visible : Visibility.Collapsed;
        int matchIndex = -1;
        for (int i = 0; i < AutoReconnectIntervalCombo.Items.Count; i++)
        {
            if (((ComboBoxItem)AutoReconnectIntervalCombo.Items[i]).Tag is string tag &&
                int.TryParse(tag, out int seconds) && seconds == settings.AutoReconnectRetryIntervalSeconds)
            {
                matchIndex = i;
                break;
            }
        }
        AutoReconnectIntervalCombo.SelectedIndex = matchIndex;

        _loadingPreferences = false;
    }

    private async void KillSwitchCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingPreferences) return;
        bool enabled = KillSwitchCheckBox.IsChecked == true;
        AppSettings.Current.KillSwitchEnabled = enabled;

        if (enabled && AppSettings.Current.AutoReconnectEnabled)
        {
            AppSettings.Current.AutoReconnectEnabled = false;
            _loadingPreferences = true;
            AutoReconnectCheckBox.IsChecked = false;
            _loadingPreferences = false;
        }

        AppSettings.Save();
        if (!enabled) await KillSwitchService.DisarmAsync();
    }

    private void AutoReconnectCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingPreferences) return;
        bool enabled = AutoReconnectCheckBox.IsChecked == true;
        AppSettings.Current.AutoReconnectEnabled = enabled;
        AutoReconnectIntervalPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        if (enabled && AppSettings.Current.KillSwitchEnabled)
        {
            AppSettings.Current.KillSwitchEnabled = false;
            _loadingPreferences = true;
            KillSwitchCheckBox.IsChecked = false;
            _loadingPreferences = false;
            _ = KillSwitchService.DisarmAsync();
        }

        AppSettings.Save();
    }

    private void AutoReconnectIntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPreferences) return;
        if (AutoReconnectIntervalCombo.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !int.TryParse(tag, out int seconds)) return;

        AppSettings.Current.AutoReconnectRetryIntervalSeconds = seconds;
        AppSettings.Save();
    }

    private void AutoConnectCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingPreferences) return;
        AppSettings.Current.AutoConnectLastServer = AutoConnectCheckBox.IsChecked == true;
        AppSettings.Save();
    }

    private void StartWithWindowsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingPreferences) return;
        bool enabled = StartWithWindowsCheckBox.IsChecked == true;

        try
        {
            StartupService.SetEnabled(enabled);
        }
        catch (Exception ex)
        {
            _loadingPreferences = true;
            StartWithWindowsCheckBox.IsChecked = !enabled;
            _loadingPreferences = false;
            ToastService.Show("Couldn't change startup setting", ex.Message, ToastKind.Error);
            return;
        }

        AppSettings.Current.StartWithWindows = enabled;
        AppSettings.Save();
    }

    private void ObfuscationCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingPreferences) return;
        bool enabled = ObfuscationCheckBox.IsChecked == true;
        AppSettings.Current.ObfuscationEnabled = enabled;
        AppSettings.Save();

        if (enabled && !NpcapService.IsInstalled())
        {
            ObfuscationStatusText.Text = "Npcap isn't installed yet - you'll need it before obfuscation can " +
                "actually turn on for your next connection. Get it from npcap.com, then reconnect.";
            ObfuscationStatusText.Visibility = Visibility.Visible;
        }
        else
        {
            ObfuscationStatusText.Text = enabled
                ? "This takes effect the next time you connect - reconnect now if you're already connected."
                : "";
            ObfuscationStatusText.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void RefreshWireGuardStatus()
    {
        bool installed = WireGuardService.IsWireGuardInstalled;
        WireGuardStatusText.Text = installed
            ? "WireGuard for Windows: installed"
            : "WireGuard for Windows: not installed";
        WireGuardMissingPanel.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
    }

    private async System.Threading.Tasks.Task CheckConnectionAsync()
    {
        try
        {
            await _api.GetMeAsync();
            ServerUrlText.Text = "Server: connected";
        }
        catch
        {
            ServerUrlText.Text = "Server: unreachable - check your internet connection";
        }
    }

    private async System.Threading.Tasks.Task LoadDeviceUsageAsync()
    {
        try
        {
            var me = await _api.GetMeAsync();
            DeviceUsageText.Text = me.DeviceLimit.HasValue
                ? $"{me.DevicesUsed} of {me.DeviceLimit} devices used"
                : $"{me.DevicesUsed} device(s) logged in (no limit on this plan)";
        }
        catch
        {
            DeviceUsageText.Text = "Couldn't load device usage.";
        }
    }

    private async void InstallWireGuardButton_Click(object sender, RoutedEventArgs e)
    {
        InstallWireGuardButton.IsEnabled = false;
        InstallStatusText.Text = "Downloading the official installer from wireguard.com...";
        try
        {
            bool installed = await WireGuardService.DownloadAndInstallAsync();
            RefreshWireGuardStatus();
            InstallStatusText.Text = installed
                ? "WireGuard installed."
                : "The installer closed but WireGuard still isn't detected - if you cancelled it, click Install again.";
        }
        catch (Exception ex)
        {
            InstallStatusText.Text = $"Couldn't install automatically: {ex.Message}. Use \"download it yourself\" instead.";
        }
        finally
        {
            InstallWireGuardButton.IsEnabled = true;
        }
    }

    private void ManualWireGuardLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(WireGuardService.OfficialDownloadPage) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            InstallStatusText.Text = $"Couldn't open the download page: {ex.Message}";
        }
    }
}
