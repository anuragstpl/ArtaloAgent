namespace ArtaloBot.Core.Models;

/// <summary>
/// Represents a custom AI agent with specific knowledge/data.
/// </summary>
public class Agent
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string Icon { get; set; } = "Robot";
    public bool IsEnabled { get; set; } = true;
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties (not stored in SQLite directly)
    public List<AgentDocument> Documents { get; set; } = [];
}

/// <summary>
/// Represents a document uploaded to an agent.
/// </summary>
public class AgentDocument
{
    public int Id { get; set; }
    public int AgentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DocumentProcessingStatus Status { get; set; } = DocumentProcessingStatus.Pending;
    public string? ErrorMessage { get; set; }
    public int ChunkCount { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }

    // Navigation
    public Agent? Agent { get; set; }
}

/// <summary>
/// Represents a chunk of text from a document with its vector embedding.
/// </summary>
public class AgentChunk
{
    public int Id { get; set; }
    public int AgentId { get; set; }
    public int DocumentId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string EmbeddingJson { get; set; } = "[]";
    public string EmbeddingModel { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Agent? Agent { get; set; }
    public AgentDocument? Document { get; set; }

    // Computed property for embedding
    public float[] Embedding
    {
        get => string.IsNullOrEmpty(EmbeddingJson) ? [] :
            System.Text.Json.JsonSerializer.Deserialize<float[]>(EmbeddingJson) ?? [];
        set => EmbeddingJson = System.Text.Json.JsonSerializer.Serialize(value);
    }
}

public enum DocumentProcessingStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

/// <summary>
/// Result of a vector search within an agent's knowledge base.
/// </summary>
public class AgentSearchResult
{
    public AgentChunk Chunk { get; set; } = null!;
    public float Similarity { get; set; }
    public string DocumentName { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public int AgentId { get; set; }
}

/// <summary>
/// Links an agent to a channel for knowledge-based responses.
/// </summary>
public class ChannelAgentAssignment
{
    public int Id { get; set; }
    public ChannelType ChannelType { get; set; }
    public int AgentId { get; set; }
    public int Priority { get; set; } = 0; // Higher priority = checked first
    public bool IsEnabled { get; set; } = true;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Agent? Agent { get; set; }
}
