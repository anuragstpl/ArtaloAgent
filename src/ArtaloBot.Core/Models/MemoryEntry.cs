namespace ArtaloBot.Core.Models;

/// <summary>
/// Represents a stored memory entry with its vector embedding for semantic search.
/// </summary>
public class MemoryEntry
{
    public int Id { get; set; }

    /// <summary>Session this memory belongs to (null = global/cross-session memory).</summary>
    public int? SessionId { get; set; }

    public MessageRole Role { get; set; }

    /// <summary>The original text content of the message.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>JSON-serialized float[] embedding vector.</summary>
    public string EmbeddingJson { get; set; } = string.Empty;

    /// <summary>Which embedding model/provider was used to generate the embedding.</summary>
    public string EmbeddingModel { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Optional metadata tag (e.g. "fact", "preference", "context").</summary>
    public string Tag { get; set; } = string.Empty;
}

/// <summary>
/// A memory entry paired with its cosine similarity score for a given query.
/// </summary>
public record MemorySearchResult(MemoryEntry Entry, double Score);

/// <summary>
/// Settings for the vector memory subsystem.
/// </summary>
public class MemorySettings
{
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Which provider / endpoint to use for generating embeddings.
    /// "ollama" uses local Ollama /api/embeddings.
    /// "openai" uses OpenAI text-embedding-3-small.
    /// "simple" uses a built-in TF-IDF fallback (no API needed).
    /// </summary>
    public string EmbeddingProvider { get; set; } = "ollama";

    /// <summary>Embedding model name (relevant for Ollama and OpenAI providers).</summary>
    public string EmbeddingModel { get; set; } = "mxbai-embed-large:latest";

    /// <summary>Cosine similarity threshold (0–1). Memories below this score are excluded.</summary>
    public double SimilarityThreshold { get; set; } = 0.40; // Lowered for better semantic matching

    /// <summary>Maximum number of relevant memories to inject into each prompt.</summary>
    public int MaxMemoriesToInject { get; set; } = 10;

    /// <summary>Maximum total number of memories stored globally before the oldest are pruned.</summary>
    public int MaxMemories { get; set; } = 1000;

    /// <summary>Whether to search memories across all sessions (global) or per-session only.</summary>
    public bool CrossSessionMemory { get; set; } = true;

    /// <summary>Whether to store memories globally (true) or per-session (false).</summary>
    public bool StoreGlobally { get; set; } = true;
}
