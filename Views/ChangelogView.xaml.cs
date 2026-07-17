using System.Windows;
using System.Windows.Controls;
using MonolithVpnClient.Models;
using MonolithVpnClient.Services;

namespace MonolithVpnClient.Views;

public partial class ChangelogView : UserControl
{
    private readonly ApiClient _api;

    public ChangelogView(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        EmptyText.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var entries = await _api.GetChangelogAsync();
            EntryList.ItemsSource = entries;
            EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (ApiException)
        {
            ErrorText.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
