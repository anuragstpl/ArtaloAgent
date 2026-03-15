using ArtaloBot.Core.Models;

namespace ArtaloBot.Core.Interfaces;

/// <summary>
/// Service for managing custom AI agents and their knowledge bases.
/// </summary>
public interface IAgentService
{
    // Agent CRUD
    Task<List<Agent>> GetAllAgentsAsync();
    Task<Agent?> GetAgentAsync(int id);
    Task<Agent?> GetDefaultAgentAsync();
    Task<Agent> CreateAgentAsync(Agent agent);
    Task<Agent> UpdateAgentAsync(Agent agent);
    Task DeleteAgentAsync(int id);
    Task SetDefaultAgentAsync(int id);

    // Document Management
    Task<AgentDocument> AddDocumentAsync(int agentId, string filePath, string fileName);
    Task<List<AgentDocument>> GetDocumentsAsync(int agentId);
    Task DeleteDocumentAsync(int documentId);
    Task ReprocessDocumentAsync(int documentId);

    // Knowledge Search
    Task<List<AgentSearchResult>> SearchKnowledgeAsync(int agentId, string query, int maxResults = 5);
    Task<List<AgentSearchResult>> SearchAllAgentsAsync(string query, int maxResults = 5);

    // Stats
    Task<int> GetTotalChunksAsync(int agentId);
    Task<int> GetTotalDocumentsAsync(int agentId);

    // Channel-Agent Assignments
    Task<List<ChannelAgentAssignment>> GetChannelAssignmentsAsync(ChannelType channelType);
    Task<List<Agent>> GetAgentsForChannelAsync(ChannelType channelType);
    Task AssignAgentToChannelAsync(int agentId, ChannelType channelType, int priority = 0);
    Task UnassignAgentFromChannelAsync(int agentId, ChannelType channelType);
    Task<List<AgentSearchResult>> SearchChannelKnowledgeAsync(ChannelType channelType, string query, int maxResults = 10, float minSimilarity = 0.15f);

    // Channel LLM Configuration
    Task<ChannelLLMConfig?> GetChannelLLMConfigAsync(ChannelType channelType);
    Task<List<ChannelLLMConfig>> GetAllChannelLLMConfigsAsync();
    Task<ChannelLLMConfig> SaveChannelLLMConfigAsync(ChannelLLMConfig config);
}

/// <summary>
/// Service for processing documents into chunks and embeddings.
/// </summary>
public interface IDocumentProcessor
{
    /// <summary>
    /// Supported file extensions.
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Check if a file type is supported.
    /// </summary>
    bool IsSupported(string fileName);

    /// <summary>
    /// Extract text content from a document.
    /// </summary>
    Task<string> ExtractTextAsync(string filePath);

    /// <summary>
    /// Split text into chunks suitable for embedding.
    /// Uses simple sentence-based chunking.
    /// </summary>
    List<DocumentChunk> ChunkText(string text, int chunkSize = 500, int overlap = 50);

    /// <summary>
    /// Split text into semantic chunks using LLM for better context preservation.
    /// Identifies natural topic boundaries and groups related content.
    /// </summary>
    Task<List<DocumentChunk>> SemanticChunkTextAsync(string text, int targetChunkSize = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process a document: extract text, chunk it, and generate embeddings.
    /// </summary>
    Task<List<AgentChunk>> ProcessDocumentAsync(
        int agentId,
        int documentId,
        string filePath,
        IProgress<DocumentProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enable or disable semantic (LLM-based) chunking.
    /// When enabled, documents will be chunked using LLM for better context preservation.
    /// </summary>
    bool UseSemanticChunking { get; set; }
}

/// <summary>
/// Represents a chunk of text extracted from a document.
/// </summary>
public class DocumentChunk
{
    public string Content { get; set; } = string.Empty;
    public int Index { get; set; }
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

/// <summary>
/// Progress information for document processing.
/// </summary>
public class DocumentProcessingProgress
{
    public string Stage { get; set; } = string.Empty;
    public int CurrentChunk { get; set; }
    public int TotalChunks { get; set; }
    public double Percentage => TotalChunks > 0 ? (double)CurrentChunk / TotalChunks * 100 : 0;
}
