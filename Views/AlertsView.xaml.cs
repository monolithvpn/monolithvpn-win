using System.Windows;
using System.Windows.Controls;
using MonolithVpnClient.Services;

namespace MonolithVpnClient.Views;

public partial class AlertsView : UserControl
{
    public AlertsView()
    {
        InitializeComponent();
        AlertList.ItemsSource = AlertLog.Entries;
        AlertLog.Entries.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();
    }

    private void UpdateEmptyState() =>
        EmptyText.Visibility = AlertLog.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}
