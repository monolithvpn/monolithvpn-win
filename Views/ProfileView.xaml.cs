using System.Windows.Controls;
using MonolithVpnClient.Services;

namespace MonolithVpnClient.Views;

public partial class ProfileView : UserControl
{
    private readonly ApiClient _api;

    public ProfileView(ApiClient api)
    {
        InitializeComponent();
        _api = api;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        try
        {
            var me = await _api.GetMeAsync();
            UsernameText.Text = me.Username;

            if (me.Plan is null)
            {
                PlanNameText.Visibility = System.Windows.Visibility.Collapsed;
                DeviceLimitText.Visibility = System.Windows.Visibility.Collapsed;
                NoPlanText.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                PlanNameText.Text = me.Plan.Tier ?? me.Plan.Name;
                DeviceLimitText.Text = me.Plan.DeviceLimit.HasValue
                    ? $"Device limit: {me.Plan.DeviceLimit}"
                    : "";
            }
        }
        catch (ApiException)
        {
            UsernameText.Text = "Couldn't load your profile - try again shortly.";
        }
        catch (Exception)
        {
            UsernameText.Text = "Couldn't reach the server to load your profile.";
        }
    }
}
