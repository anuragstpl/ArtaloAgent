using ArtaloBot.Core.Models;

namespace ArtaloBot.Core.Interfaces;

/// <summary>
/// Service for storing, retrieving, and managing conversation memories using vector embeddings.
/// </summary>
public interface IMemoryService
{
    /// <summary>Generate an embedding vector for a piece of text using the configured provider.</summary>
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Store a message as a memory entry (will compute embedding automatically).</summary>
    Task StoreMemoryAsync(
        string content,
        MessageRole role,
        int? sessionId = null,
        string tag = "",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for memories semantically similar to the query text.
    /// Returns results ordered by descending similarity score.
    /// </summary>
    Task<IReadOnlyList<MemorySearchResult>> SearchMemoriesAsync(
        string query,
        int? sessionId = null,
        int topK = 5,
        double? similarityThreshold = null,
        CancellationToken cancellationToken = default);

    /// <summary>Delete all memories for the given session (or all global memories if sessionId is null).</summary>
    Task ClearMemoriesAsync(int? sessionId = null, CancellationToken cancellationToken = default);

    /// <summary>Delete all memories regardless of session.</summary>
    Task ClearAllMemoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Return total count of stored memories.</summary>
    Task<int> GetMemoryCountAsync(int? sessionId = null, CancellationToken cancellationToken = default);

    /// <summary>Whether the memory service is properly configured and ready to use.</summary>
    bool IsReady { get; }
}
