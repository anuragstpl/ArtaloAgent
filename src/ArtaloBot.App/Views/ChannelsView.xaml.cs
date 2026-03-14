using System.Windows.Controls;
using ArtaloBot.App.ViewModels;

namespace ArtaloBot.App.Views;

public partial class ChannelsView : UserControl
{
    public ChannelsView()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is ChannelsViewModel vm)
            {
                await vm.LoadAgentsAsync();
            }
        };
    }
}
