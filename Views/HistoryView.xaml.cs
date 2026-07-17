using System.Windows;
using System.Windows.Controls;
using MonolithVpnClient.Services;

namespace MonolithVpnClient.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
        HistoryList.ItemsSource = ConnectionHistory.Entries;
        UpdateEmptyState();
        ConnectionHistory.Entries.CollectionChanged += (_, _) => UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyText.Visibility = ConnectionHistory.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
