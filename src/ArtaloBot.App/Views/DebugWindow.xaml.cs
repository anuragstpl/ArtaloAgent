using System.Windows;
using System.Windows.Controls;
using ArtaloBot.App.ViewModels;

namespace ArtaloBot.App.Views;

public partial class DebugWindow : Window
{
    private readonly DebugViewModel _viewModel;

    public DebugWindow(DebugViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // Subscribe to scroll request
        _viewModel.ScrollRequested += OnScrollRequested;

        Closed += (_, _) =>
        {
            _viewModel.ScrollRequested -= OnScrollRequested;
        };
    }

    private void OnScrollRequested(object? sender, EventArgs e)
    {
        if (_viewModel.AutoScroll)
        {
            LogScrollViewer.ScrollToEnd();
        }
    }
}
