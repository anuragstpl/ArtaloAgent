using ArtaloBot.Core.Models;

namespace ArtaloBot.Core.Interfaces;

public interface IChatRepository
{
    Task<ChatSession> CreateSessionAsync(ChatSession session, CancellationToken cancellationToken = default);
    Task<ChatSession?> GetSessionAsync(int sessionId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ChatSession>> GetAllSessionsAsync(bool includeArchived = false, CancellationToken cancellationToken = default);
    Task UpdateSessionAsync(ChatSession session, CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(int sessionId, CancellationToken cancellationToken = default);

    Task<ChatMessage> AddMessageAsync(ChatMessage message, CancellationToken cancellationToken = default);
    Task<IEnumerable<ChatMessage>> GetSessionMessagesAsync(int sessionId, CancellationToken cancellationToken = default);
    Task UpdateMessageAsync(ChatMessage message, CancellationToken cancellationToken = default);
    Task DeleteMessageAsync(int messageId, CancellationToken cancellationToken = default);
}
