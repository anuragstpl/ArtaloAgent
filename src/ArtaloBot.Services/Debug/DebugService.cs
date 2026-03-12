using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using ArtaloBot.Core.Interfaces;

namespace ArtaloBot.Services.Debug;

/// <summary>
/// Debug logging service that stores entries for display in the debug window.
/// Thread-safe for use from background tasks.
/// </summary>
public class DebugService : IDebugService
{
    private readonly ConcurrentQueue<DebugLogEntry> _pendingEntries = new();
    private const int MaxEntries = 1000;

    public ObservableCollection<DebugLogEntry> Entries { get; } = [];
    public bool IsEnabled { get; set; } = true;

    public event EventHandler<DebugLogEntry>? LogAdded;

    public void Log(DebugLogLevel level, string category, string message, string? details = null)
    {
        if (!IsEnabled) return;

        var entry = new DebugLogEntry
        {
            Level = level,
            Category = category,
            Message = message,
            Details = details
        };

        // Queue the entry (thread-safe)
        _pendingEntries.Enqueue(entry);

        // Raise event for subscribers to handle UI thread marshaling
        LogAdded?.Invoke(this, entry);
    }

    /// <summary>
    /// Process pending entries and add them to the observable collection.
    /// Call this from the UI thread.
    /// </summary>
    public void ProcessPendingEntries()
    {
        while (_pendingEntries.TryDequeue(out var entry))
        {
            // Prune old entries if needed
            while (Entries.Count >= MaxEntries)
            {
                Entries.RemoveAt(0);
            }
            Entries.Add(entry);
        }
    }

    public void Info(string category, string message, string? details = null)
        => Log(DebugLogLevel.Info, category, message, details);

    public void Warning(string category, string message, string? details = null)
        => Log(DebugLogLevel.Warning, category, message, details);

    public void Error(string category, string message, string? details = null)
        => Log(DebugLogLevel.Error, category, message, details);

    public void Success(string category, string message, string? details = null)
        => Log(DebugLogLevel.Success, category, message, details);

    public void Clear()
    {
        // Clear both the queue and the collection
        while (_pendingEntries.TryDequeue(out _)) { }
        Entries.Clear();
    }
}
