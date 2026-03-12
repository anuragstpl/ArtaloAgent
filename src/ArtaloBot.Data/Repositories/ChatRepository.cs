using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ArtaloBot.Data.Repositories;

public class ChatRepository : IChatRepository
{
    private readonly AppDbContext _context;

    public ChatRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ChatSession> CreateSessionAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        _context.ChatSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<ChatSession?> GetSessionAsync(int sessionId, CancellationToken cancellationToken = default)
    {
        return await _context.ChatSessions
            .Include(s => s.Messages.OrderBy(m => m.Timestamp))
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
    }

    public async Task<IEnumerable<ChatSession>> GetAllSessionsAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var query = _context.ChatSessions.AsQueryable();

        if (!includeArchived)
        {
            query = query.Where(s => !s.IsArchived);
        }

        return await query
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateSessionAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        session.UpdatedAt = DateTime.UtcNow;
        _context.ChatSessions.Update(session);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteSessionAsync(int sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _context.ChatSessions.FindAsync([sessionId], cancellationToken);
        if (session != null)
        {
            _context.ChatSessions.Remove(session);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<ChatMessage> AddMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        _context.ChatMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);

        // Update session timestamp
        var session = await _context.ChatSessions.FindAsync([message.SessionId], cancellationToken);
        if (session != null)
        {
            session.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return message;
    }

    public async Task<IEnumerable<ChatMessage>> GetSessionMessagesAsync(int sessionId, CancellationToken cancellationToken = default)
    {
        return await _context.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        _context.ChatMessages.Update(message);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteMessageAsync(int messageId, CancellationToken cancellationToken = default)
    {
        var message = await _context.ChatMessages.FindAsync([messageId], cancellationToken);
        if (message != null)
        {
            _context.ChatMessages.Remove(message);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
