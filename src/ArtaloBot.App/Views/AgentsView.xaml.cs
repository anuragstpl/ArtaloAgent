using System.Windows;
using System.Windows.Controls;
using ArtaloBot.App.ViewModels;

namespace ArtaloBot.App.Views;

public partial class AgentsView : UserControl
{
    public AgentsView()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is AgentsViewModel vm)
            {
                await vm.LoadAgentsAsync();
            }
        };
    }

    private void ShowFormatHelp_Click(object sender, RoutedEventArgs e)
    {
        FormatHelpCard.Visibility = Visibility.Visible;
    }

    private void HideFormatHelp_Click(object sender, RoutedEventArgs e)
    {
        FormatHelpCard.Visibility = Visibility.Collapsed;
    }
}
