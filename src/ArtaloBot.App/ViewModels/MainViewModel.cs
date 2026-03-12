using ArtaloBot.App.Services;
using ArtaloBot.App.Views;
using ArtaloBot.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArtaloBot.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IThemeService _themeService;
    private readonly IDebugService _debugService;
    private DebugWindow? _debugWindow;

    [ObservableProperty]
    private ObservableObject? _currentView;

    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private bool _isSidebarExpanded = true;

    [ObservableProperty]
    private int _selectedNavIndex;

    [ObservableProperty]
    private bool _isDebugWindowOpen;

    public ChatViewModel ChatViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }
    public SessionsViewModel SessionsViewModel { get; }
    public DebugViewModel DebugViewModel { get; }
    public MCPViewModel MCPViewModel { get; }

    public MainViewModel(
        INavigationService navigationService,
        IThemeService themeService,
        IDebugService debugService,
        ChatViewModel chatViewModel,
        SettingsViewModel settingsViewModel,
        SessionsViewModel sessionsViewModel,
        DebugViewModel debugViewModel,
        MCPViewModel mcpViewModel)
    {
        _navigationService = navigationService;
        _themeService = themeService;
        _debugService = debugService;

        ChatViewModel = chatViewModel;
        SettingsViewModel = settingsViewModel;
        SessionsViewModel = sessionsViewModel;
        DebugViewModel = debugViewModel;
        MCPViewModel = mcpViewModel;

        _currentView = chatViewModel;
        _isDarkTheme = _themeService.IsDarkTheme;

        _navigationService.NavigationChanged += OnNavigationChanged;

        _debugService.Info("App", "ArtaloBot started");
    }

    private void OnNavigationChanged(object? sender, Type viewModelType)
    {
        if (viewModelType == typeof(ChatViewModel))
            CurrentView = ChatViewModel;
        else if (viewModelType == typeof(SettingsViewModel))
            CurrentView = SettingsViewModel;
        else if (viewModelType == typeof(MCPViewModel))
            CurrentView = MCPViewModel;
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        _themeService.ToggleTheme();
        IsDarkTheme = _themeService.IsDarkTheme;
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarExpanded = !IsSidebarExpanded;
    }

    [RelayCommand]
    private void NavigateToChat()
    {
        CurrentView = ChatViewModel;
        SelectedNavIndex = 0;
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        CurrentView = SettingsViewModel;
        SelectedNavIndex = 1;
    }

    [RelayCommand]
    private void NavigateToMCP()
    {
        CurrentView = MCPViewModel;
        SelectedNavIndex = 2;
    }

    [RelayCommand]
    private async Task NewChat()
    {
        await SessionsViewModel.CreateNewSessionAsync();
        CurrentView = ChatViewModel;
        SelectedNavIndex = 0;
    }

    [RelayCommand]
    private void OpenDebugWindow()
    {
        if (_debugWindow == null || !_debugWindow.IsLoaded)
        {
            _debugWindow = new DebugWindow(DebugViewModel);
            _debugWindow.Closed += (_, _) =>
            {
                IsDebugWindowOpen = false;
                _debugWindow = null;
            };
            _debugWindow.Show();
            IsDebugWindowOpen = true;
            _debugService.Info("App", "Debug window opened");
        }
        else
        {
            _debugWindow.Activate();
        }
    }
}
