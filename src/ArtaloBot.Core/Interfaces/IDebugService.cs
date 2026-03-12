using System.Collections.ObjectModel;

namespace ArtaloBot.Core.Interfaces;

/// <summary>
/// Service for logging debug information that can be displayed in a debug window.
/// </summary>
public interface IDebugService
{
    /// <summary>All logged entries.</summary>
    ObservableCollection<DebugLogEntry> Entries { get; }

    /// <summary>Whether debug logging is enabled.</summary>
    bool IsEnabled { get; set; }

    /// <summary>Log a debug entry.</summary>
    void Log(DebugLogLevel level, string category, string message, string? details = null);

    /// <summary>Log an info message.</summary>
    void Info(string category, string message, string? details = null);

    /// <summary>Log a warning message.</summary>
    void Warning(string category, string message, string? details = null);

    /// <summary>Log an error message.</summary>
    void Error(string category, string message, string? details = null);

    /// <summary>Log a success message.</summary>
    void Success(string category, string message, string? details = null);

    /// <summary>Clear all log entries.</summary>
    void Clear();

    /// <summary>Event raised when a new log entry is added.</summary>
    event EventHandler<DebugLogEntry>? LogAdded;
}

public enum DebugLogLevel
{
    Info,
    Warning,
    Error,
    Success
}

public class DebugLogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public DebugLogLevel Level { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Details { get; init; }

    public string TimestampFormatted => Timestamp.ToString("HH:mm:ss.fff");
    public string LevelIcon => Level switch
    {
        DebugLogLevel.Info => "Info",
        DebugLogLevel.Warning => "Warning",
        DebugLogLevel.Error => "Error",
        DebugLogLevel.Success => "Check",
        _ => "Info"
    };
}
