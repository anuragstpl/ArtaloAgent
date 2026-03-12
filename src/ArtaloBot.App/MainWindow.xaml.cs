using System.Windows;
using ArtaloBot.App.ViewModels;

namespace ArtaloBot.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (_, _) =>
        {
            await viewModel.SessionsViewModel.LoadSessionsAsync();
            await viewModel.SettingsViewModel.LoadSettingsAsync();
            await viewModel.ChatViewModel.LoadAvailableModelsAsync();
        };
    }
}
