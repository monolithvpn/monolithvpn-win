using System.Windows;
using Application = System.Windows.Application;

namespace MonolithVpnClient.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly Window _window;
    private bool _exiting;

    public TrayIconService(Window window)
    {
        _window = window;

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open MonolithVPN", null, (_, _) => Restore());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = "MonolithVPN",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => Restore();

        _window.StateChanged += OnWindowStateChanged;
        _window.Closing += OnWindowClosing;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized)
            _window.Hide();
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting) return;
        e.Cancel = true;
        _window.Hide();
    }

    private void Restore()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private async void ExitApp()
    {
        if (ConnectionManager.ConnectedServerId is int serverId)
        {
            string name = ConnectionState.Current?.Name ?? "the server";
            bool confirmed = ConfirmDialogService.ShowYesNo(
                "Exit MonolithVPN?",
                $"You're connected to {name}. Exiting MonolithVPN will disconnect the tunnel.",
                confirmText: "Exit anyway", cancelText: "Stay connected");
            if (!confirmed) return;

            try { await ConnectionManager.DisconnectAsync(serverId, name); }
            catch { }
        }

        try { await KillSwitchService.DisarmAsync(); }
        catch { }
        try { await ObfuscationService.StopAsync(); }
        catch { }

        _exiting = true;
        _icon.Visible = false;
        _icon.Dispose();
        Application.Current.Shutdown();
    }

    public void AllowRealClose()
    {
        _exiting = true;
        _icon.Visible = false;
    }

    public void Dispose()
    {
        _icon.Dispose();
    }
}
