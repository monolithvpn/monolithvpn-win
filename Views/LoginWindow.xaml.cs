using System.Windows;
using System.Windows.Input;
using MonolithVpnClient.Services;

namespace MonolithVpnClient.Views;

public partial class LoginWindow : Window
{
    private readonly ApiClient _api = new();
    private UpdateCheckResult? _pendingUpdate;

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _ = CheckForUpdateAsync();
            TryAutoLogin();
        };
    }

    private async void TryAutoLogin()
    {
        var saved = TokenStorage.Load();
        if (saved is null) return;

        _api.SetToken(saved);
        try
        {
            var me = await _api.GetMeAsync();
            OpenMainWindow(me.Username);
        }
        catch (ApiException ex)
        {
            if (ex.RequiresUpdate)
            {
                _ = CheckForUpdateAsync();
                return;
            }

            TokenStorage.Clear();
            _api.SetToken(null);
        }
        catch (Exception)
        {
        }
    }

    private async Task CheckForUpdateAsync()
    {
        if (_pendingUpdate is not null) return;
        var result = await UpdateService.CheckAsync(_api);
        if (!result.UpdateAvailable || result.DownloadUrl is null) return;
        _pendingUpdate = result;

        bool update = ConfirmDialogService.ShowYesNo(
            "Update available",
            $"Version {result.LatestVersion} is available - you're on {result.CurrentVersion}. "
                + "Would you like to update now? The app will close and reopen automatically - "
                + "Windows may ask you to approve the install.",
            confirmText: "Update now", cancelText: "Not now");
        if (!update) return;

        ToastService.Show("Updating", "Downloading the update...", ToastKind.Info);

        var updateApi = _api.Token is not null ? _api : new ApiClient();
        if (updateApi.Token is null && TokenStorage.Load() is string saved) updateApi.SetToken(saved);

        var install = await UpdateService.PerformAutoUpdateAsync(updateApi, result.DownloadUrl);
        if (install.Success)
        {
            Application.Current.Shutdown();
            return;
        }

        ToastService.Show(
            "Couldn't update automatically",
            "Opening your browser so you can download it there instead.", ToastKind.Info);
        UpdateService.OpenDownload(result.DownloadUrl);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var username = UsernameBox.Text.Trim();
        var password = PasswordInput.Password;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("Enter your username and password.");
            return;
        }

        LoginButton.IsEnabled = false;
        LoginButton.Content = "Signing in...";
        try
        {
            var result = await _api.LoginAsync(username, password);
            _api.SetToken(result.Token);
            if (RememberMeCheckBox.IsChecked == true)
                TokenStorage.Save(result.Token);
            else
                TokenStorage.Clear();

            FreeModeState.LastKnownEnabled = result.FreeMode;
            OpenMainWindow(result.Username);
            if (result.FreeMode)
            {
                ToastService.Show("Free mode", result.FreeModeMessage ?? "Free mode is currently active.", ToastKind.Info);
            }
        }
        catch (ApiException ex)
        {
            ShowError(ex.Message);
            if (ex.RequiresUpdate) _ = CheckForUpdateAsync();
        }
        catch (Exception)
        {
            ShowError("Couldn't reach the server. Check your connection and try again.");
        }
        finally
        {
            LoginButton.IsEnabled = true;
            LoginButton.Content = "Log in";
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private async void OpenMainWindow(string username)
    {
        try
        {
            await ConnectionManager.TryAdoptExistingTunnelAsync(_api);

            var main = new MainWindow(_api, username);
            Application.Current.MainWindow = main;
            main.Show();
            ToastService.Show("Welcome back", $"Signed in as {username}.", ToastKind.Success);
            Close();
        }
        catch (Exception ex)
        {
            ShowError($"Signed in, but couldn't open the app: {ex.Message}. Try again.");
        }
    }

    private void RegisterLink_Click(object sender, RoutedEventArgs e) => WebsiteLinks.Open("/auth/register");

    private void RecoverLink_Click(object sender, RoutedEventArgs e) => WebsiteLinks.Open("/auth/recover");
}
