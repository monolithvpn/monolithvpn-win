using System.Windows;
using MonolithVpnClient.Views;

namespace MonolithVpnClient.Services;

public static class ConfirmDialogService
{
    public static bool ShowYesNo(string title, string message, string confirmText = "Yes", string cancelText = "No")
    {
        var owner = Application.Current?.MainWindow;
        var dialog = new ConfirmWindow(title, message, confirmText, cancelText);
        if (owner is { IsVisible: true } && !ReferenceEquals(owner, dialog))
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        return dialog.ShowDialog() == true;
    }

    public static void ShowInfo(string title, string message, string okText = "Got it")
    {
        var owner = Application.Current?.MainWindow;
        var dialog = new ConfirmWindow(title, message, okText, null);
        if (owner is { IsVisible: true } && !ReferenceEquals(owner, dialog))
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dialog.ShowDialog();
    }
}
