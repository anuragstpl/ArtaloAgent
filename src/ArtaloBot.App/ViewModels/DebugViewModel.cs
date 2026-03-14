using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Services.Debug;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArtaloBot.App.ViewModels;

public partial class DebugViewModel : ObservableObject
{
    private readonly IDebugService _debugService;
    private readonly DebugService _debugServiceImpl;

    public ObservableCollection<DebugLogEntry> Entries => _debugService.Entries;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private bool _isTopmost = false;

    [ObservableProperty]
    private int _entryCount;

    public event EventHandler? ScrollRequested;

    public DebugViewModel(IDebugService debugService)
    {
        _debugService = debugService;
        _debugServiceImpl = debugService as DebugService
            ?? throw new InvalidOperationException("Expected DebugService implementation");

        // Subscribe to log events
        _debugService.LogAdded += OnLogAdded;

        // Update entry count when collection changes
        _debugService.Entries.CollectionChanged += (_, _) =>
        {
            EntryCount = _debugService.Entries.Count;
        };
    }

    private void OnLogAdded(object? sender, DebugLogEntry e)
    {
        // Process pending entries on UI thread
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            _debugServiceImpl.ProcessPendingEntries();
            ScrollRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    [RelayCommand]
    private void Clear()
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            _debugService.Clear();
        });
    }

    [RelayCommand]
    private void CopyAll()
    {
        var sb = new StringBuilder();
        foreach (var entry in Entries)
        {
            sb.AppendLine($"[{entry.TimestampFormatted}] [{entry.Level}] [{entry.Category}] {entry.Message}");
            if (!string.IsNullOrEmpty(entry.Details))
            {
                sb.AppendLine($"    Details: {entry.Details}");
            }
        }

        if (sb.Length > 0)
        {
            try
            {
                Clipboard.SetText(sb.ToString());
                _debugService.Success("Debug", "Copied all log entries to clipboard");
            }
            catch (Exception ex)
            {
                _debugService.Error("Debug", "Failed to copy to clipboard", ex.Message);
            }
        }
    }
}
