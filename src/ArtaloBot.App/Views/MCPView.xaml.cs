using System.Windows.Controls;
using ArtaloBot.App.ViewModels;

namespace ArtaloBot.App.Views;

public partial class MCPView : UserControl
{
    public MCPView()
    {
        InitializeComponent();
        DataContext = App.GetService<MCPViewModel>();
        Loaded += MCPView_Loaded;
    }

    private async void MCPView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is MCPViewModel vm)
        {
            await vm.LoadServersAsync();
        }
    }
}
