using System.Windows;

namespace MonolithVpnClient.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string message, string confirmText, string? cancelText)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        if (cancelText is null)
            CancelButton.Visibility = Visibility.Collapsed;
        else
            CancelButton.Content = cancelText;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
