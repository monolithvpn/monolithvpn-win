using System.Windows;
using System.Windows.Controls;
using MonolithVpnClient.Services;

namespace MonolithVpnClient.Views;

public partial class HelpView : UserControl
{
    public HelpView()
    {
        InitializeComponent();
    }

    private void OpenChat_Click(object sender, RoutedEventArgs e) => WebsiteLinks.Open("/contact");

    private void OpenContact_Click(object sender, RoutedEventArgs e) => WebsiteLinks.Open("/contact");

    private void OpenTickets_Click(object sender, RoutedEventArgs e) => WebsiteLinks.Open("/support/tickets");

    private void OpenTerms_Click(object sender, RoutedEventArgs e) => WebsiteLinks.Open("/legal/terms");

    private void OpenPrivacy_Click(object sender, RoutedEventArgs e) => WebsiteLinks.Open("/legal/privacy");
}
