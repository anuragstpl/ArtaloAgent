using ArtaloBot.Core.Models;

namespace ArtaloBot.Core.Events;

public class StreamingTextEventArgs : EventArgs
{
    public string Text { get; }
    public bool IsComplete { get; }

    public StreamingTextEventArgs(string text, bool isComplete = false)
    {
        Text = text;
        IsComplete = isComplete;
    }
}

public class MessageReceivedEventArgs : EventArgs
{
    public ChatMessage Message { get; }

    public MessageReceivedEventArgs(ChatMessage message)
    {
        Message = message;
    }
}

public class SessionChangedEventArgs : EventArgs
{
    public ChatSession Session { get; }
    public SessionChangeType ChangeType { get; }

    public SessionChangedEventArgs(ChatSession session, SessionChangeType changeType)
    {
        Session = session;
        ChangeType = changeType;
    }
}

public enum SessionChangeType
{
    Created,
    Updated,
    Deleted,
    Selected
}
