using System.Windows;
using System.Windows.Input;
using ArtaloBot.App.ViewModels;

namespace ArtaloBot.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += async (_, _) =>
        {
            await viewModel.SessionsViewModel.LoadSessionsAsync();
            await viewModel.SettingsViewModel.LoadSettingsAsync();
            await viewModel.ChatViewModel.LoadAvailableModelsAsync();
            await viewModel.ChatViewModel.LoadAvailableAgentsAsync();
            await viewModel.ChannelsViewModel.LoadAgentsAsync();
        };
    }

    private void OnChatTileClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.NavigateToChatCommand.Execute(null);
    }

    private void OnSettingsTileClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.NavigateToSettingsCommand.Execute(null);
    }

    private void OnSkillsTileClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.NavigateToMCPCommand.Execute(null);
    }

    private void OnChannelsTileClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.NavigateToChannelsCommand.Execute(null);
    }

    private void OnAgentsTileClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.NavigateToAgentsCommand.Execute(null);
    }

    private void OnDebugTileClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.OpenDebugWindowCommand.Execute(null);
    }

    private void OnThemeTileClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.ToggleThemeCommand.Execute(null);
    }
}
