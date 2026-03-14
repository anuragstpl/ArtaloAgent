using System.Text.Json;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using ArtaloBot.Data;
using Microsoft.EntityFrameworkCore;

namespace ArtaloBot.Services.Agents;

/// <summary>
/// Service for managing custom AI agents and their knowledge bases.
/// </summary>
public class AgentService : IAgentService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IMemoryService _memoryService;
    private readonly IDebugService? _debugService;
    private readonly string _documentsPath;

    public AgentService(
        IDbContextFactory<AppDbContext> dbFactory,
        IDocumentProcessor documentProcessor,
        IMemoryService memoryService,
        IDebugService? debugService = null)
    {
        _dbFactory = dbFactory;
        _documentProcessor = documentProcessor;
        _memoryService = memoryService;
        _debugService = debugService;

        // Set up documents storage path
        _documentsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ArtaloBot",
            "AgentDocuments");

        Directory.CreateDirectory(_documentsPath);
    }

    #region Agent CRUD

    public async Task<List<Agent>> GetAllAgentsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Agents
            .Include(a => a.Documents)
            .OrderBy(a => a.Name)
            .ToListAsync();
    }

    public async Task<Agent?> GetAgentAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Agents
            .Include(a => a.Documents)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<Agent?> GetDefaultAgentAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Agents
            .Include(a => a.Documents)
            .FirstOrDefaultAsync(a => a.IsDefault && a.IsEnabled);
    }

    public async Task<Agent> CreateAgentAsync(Agent agent)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        agent.CreatedAt = DateTime.UtcNow;
        agent.UpdatedAt = DateTime.UtcNow;

        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        _debugService?.Success("AgentService", $"Created agent: {agent.Name}", $"ID: {agent.Id}");

        return agent;
    }

    public async Task<Agent> UpdateAgentAsync(Agent agent)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var existing = await db.Agents.FindAsync(agent.Id);
        if (existing == null)
            throw new InvalidOperationException($"Agent {agent.Id} not found");

        existing.Name = agent.Name;
        existing.Description = agent.Description;
        existing.SystemPrompt = agent.SystemPrompt;
        existing.Icon = agent.Icon;
        existing.IsEnabled = agent.IsEnabled;
        existing.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        _debugService?.Info("AgentService", $"Updated agent: {agent.Name}");

        return existing;
    }

    public async Task DeleteAgentAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var agent = await db.Agents
            .Include(a => a.Documents)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (agent == null) return;

        // Delete document files
        foreach (var doc in agent.Documents)
        {
            try
            {
                if (File.Exists(doc.FilePath))
                    File.Delete(doc.FilePath);
            }
            catch { /* Ignore file deletion errors */ }
        }

        db.Agents.Remove(agent);
        await db.SaveChangesAsync();

        _debugService?.Info("AgentService", $"Deleted agent: {agent.Name}");
    }

    public async Task SetDefaultAgentAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Clear existing defaults
        var currentDefaults = await db.Agents.Where(a => a.IsDefault).ToListAsync();
        foreach (var agent in currentDefaults)
        {
            agent.IsDefault = false;
        }

        // Set new default
        var newDefault = await db.Agents.FindAsync(id);
        if (newDefault != null)
        {
            newDefault.IsDefault = true;
            _debugService?.Info("AgentService", $"Set default agent: {newDefault.Name}");
        }

        await db.SaveChangesAsync();
    }

    #endregion

    #region Document Management

    public async Task<AgentDocument> AddDocumentAsync(int agentId, string sourcePath, string fileName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var agent = await db.Agents.FindAsync(agentId);
        if (agent == null)
            throw new InvalidOperationException($"Agent {agentId} not found");

        // Copy file to documents folder
        var fileExt = Path.GetExtension(fileName);
        var storedFileName = $"{agentId}_{Guid.NewGuid():N}{fileExt}";
        var storedPath = Path.Combine(_documentsPath, storedFileName);

        File.Copy(sourcePath, storedPath, overwrite: true);

        var fileInfo = new FileInfo(storedPath);

        var document = new AgentDocument
        {
            AgentId = agentId,
            FileName = fileName,
            FilePath = storedPath,
            FileType = fileExt.TrimStart('.').ToLowerInvariant(),
            FileSize = fileInfo.Length,
            Status = DocumentProcessingStatus.Pending,
            UploadedAt = DateTime.UtcNow
        };

        db.AgentDocuments.Add(document);
        await db.SaveChangesAsync();

        _debugService?.Info("AgentService", $"Added document: {fileName}", $"Agent: {agent.Name}");

        // Process document in background
        _ = ProcessDocumentInBackgroundAsync(document.Id);

        return document;
    }

    public async Task<List<AgentDocument>> GetDocumentsAsync(int agentId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.AgentDocuments
            .Where(d => d.AgentId == agentId)
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync();
    }

    public async Task DeleteDocumentAsync(int documentId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var document = await db.AgentDocuments.FindAsync(documentId);
        if (document == null) return;

        // Delete file
        try
        {
            if (File.Exists(document.FilePath))
                File.Delete(document.FilePath);
        }
        catch { /* Ignore */ }

        // Delete chunks
        var chunks = await db.AgentChunks.Where(c => c.DocumentId == documentId).ToListAsync();
        db.AgentChunks.RemoveRange(chunks);

        db.AgentDocuments.Remove(document);
        await db.SaveChangesAsync();

        _debugService?.Info("AgentService", $"Deleted document: {document.FileName}");
    }

    public async Task ReprocessDocumentAsync(int documentId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var document = await db.AgentDocuments.FindAsync(documentId);
        if (document == null) return;

        // Delete existing chunks
        var chunks = await db.AgentChunks.Where(c => c.DocumentId == documentId).ToListAsync();
        db.AgentChunks.RemoveRange(chunks);

        // Reset status
        document.Status = DocumentProcessingStatus.Pending;
        document.ErrorMessage = null;
        document.ChunkCount = 0;

        await db.SaveChangesAsync();

        // Reprocess
        _ = ProcessDocumentInBackgroundAsync(documentId);
    }

    private async Task ProcessDocumentInBackgroundAsync(int documentId)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            var document = await db.AgentDocuments.FindAsync(documentId);
            if (document == null) return;

            document.Status = DocumentProcessingStatus.Processing;
            await db.SaveChangesAsync();

            _debugService?.Info("AgentService", $"Processing document: {document.FileName}");

            // Process document
            var chunks = await _documentProcessor.ProcessDocumentAsync(
                document.AgentId,
                document.Id,
                document.FilePath);

            // Save chunks
            db.AgentChunks.AddRange(chunks);

            document.Status = DocumentProcessingStatus.Completed;
            document.ChunkCount = chunks.Count;
            document.ProcessedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            _debugService?.Success("AgentService",
                $"Document processed: {document.FileName}",
                $"Chunks: {chunks.Count}");
        }
        catch (Exception ex)
        {
            _debugService?.Error("AgentService", $"Failed to process document {documentId}", ex.Message);

            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var document = await db.AgentDocuments.FindAsync(documentId);
                if (document != null)
                {
                    document.Status = DocumentProcessingStatus.Failed;
                    document.ErrorMessage = ex.Message;
                    await db.SaveChangesAsync();
                }
            }
            catch { /* Ignore */ }
        }
    }

    #endregion

    #region Knowledge Search

    public async Task<List<AgentSearchResult>> SearchKnowledgeAsync(int agentId, string query, int maxResults = 5)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await using var db = await _dbFactory.CreateDbContextAsync();

        _debugService?.Info("AgentSearch", $"Searching agent ID {agentId}",
            $"Query: {query.Substring(0, Math.Min(100, query.Length))}");

        // Get agent info for logging
        var agent = await db.Agents.FindAsync(agentId);
        if (agent == null)
        {
            _debugService?.Error("AgentSearch", $"Agent {agentId} not found");
            return [];
        }

        // Get all chunks for this agent
        var chunks = await db.AgentChunks
            .Where(c => c.AgentId == agentId)
            .ToListAsync();

        if (chunks.Count == 0)
        {
            _debugService?.Warning("AgentSearch", $"No chunks found for agent '{agent.Name}'",
                "Make sure documents are uploaded and processed");
            return [];
        }

        _debugService?.Info("AgentSearch", $"Agent '{agent.Name}' has {chunks.Count} chunks to search");

        // Get query embedding
        var embeddingStart = stopwatch.ElapsedMilliseconds;
        var queryEmbedding = await _memoryService.GetEmbeddingAsync(query);
        var embeddingTime = stopwatch.ElapsedMilliseconds - embeddingStart;

        if (queryEmbedding.Length == 0)
        {
            _debugService?.Error("AgentSearch", "Failed to generate query embedding");
            return [];
        }

        _debugService?.Info("AgentSearch", $"Query embedding generated",
            $"Time: {embeddingTime}ms | Dimensions: {queryEmbedding.Length}");

        // Calculate similarities for ALL chunks and rank
        var allResults = chunks
            .Select(chunk =>
            {
                var chunkEmbedding = chunk.Embedding;
                var similarity = chunkEmbedding.Length > 0
                    ? CosineSimilarity(queryEmbedding, chunkEmbedding)
                    : 0f;

                return new AgentSearchResult
                {
                    Chunk = chunk,
                    Similarity = similarity,
                    DocumentName = GetDocumentName(chunk.Metadata),
                    AgentId = agentId,
                    AgentName = agent.Name
                };
            })
            .OrderByDescending(r => r.Similarity)
            .ToList();

        // Log similarity distribution for debugging
        if (allResults.Count > 0)
        {
            var maxSim = allResults[0].Similarity;
            var minSim = allResults[^1].Similarity;
            var avgSim = allResults.Average(r => r.Similarity);
            _debugService?.Info("AgentSearch",
                $"Similarity stats: Max={maxSim:F3}, Min={minSim:F3}, Avg={avgSim:F3}");
        }

        // Use a lower threshold (0.2) to catch more potential matches
        const float similarityThreshold = 0.2f;
        var results = allResults
            .Where(r => r.Similarity > similarityThreshold)
            .Take(maxResults)
            .ToList();

        // If no results above threshold, return top chunks anyway with warning
        if (results.Count == 0 && allResults.Count > 0)
        {
            _debugService?.Warning("AgentSearch",
                $"No chunks above threshold ({similarityThreshold}), returning top {Math.Min(3, allResults.Count)} anyway");

            results = allResults.Take(Math.Min(3, allResults.Count)).ToList();
        }

        var totalTime = stopwatch.ElapsedMilliseconds;
        _debugService?.Success("AgentSearch",
            $"Search complete: {results.Count} results",
            $"Time: {totalTime}ms | Best: {(results.Count > 0 ? results[0].Similarity.ToString("F3") : "N/A")}");

        // Log the top result content preview
        if (results.Count > 0)
        {
            var preview = results[0].Chunk.Content;
            if (preview.Length > 150) preview = preview.Substring(0, 150) + "...";
            _debugService?.Info("AgentSearch", "Top result preview", preview);
        }

        return results;
    }

    public async Task<List<AgentSearchResult>> SearchAllAgentsAsync(string query, int maxResults = 5)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Get enabled agents
        var enabledAgentIds = await db.Agents
            .Where(a => a.IsEnabled)
            .Select(a => a.Id)
            .ToListAsync();

        if (enabledAgentIds.Count == 0)
            return [];

        // Get query embedding
        var queryEmbedding = await _memoryService.GetEmbeddingAsync(query);

        // Get all chunks from enabled agents
        var chunks = await db.AgentChunks
            .Where(c => enabledAgentIds.Contains(c.AgentId))
            .ToListAsync();

        // Calculate similarities and rank
        var results = chunks
            .Select(chunk => new AgentSearchResult
            {
                Chunk = chunk,
                Similarity = CosineSimilarity(queryEmbedding, chunk.Embedding),
                DocumentName = GetDocumentName(chunk.Metadata)
            })
            .Where(r => r.Similarity > 0.3f)
            .OrderByDescending(r => r.Similarity)
            .Take(maxResults)
            .ToList();

        return results;
    }

    #endregion

    #region Stats

    public async Task<int> GetTotalChunksAsync(int agentId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.AgentChunks.CountAsync(c => c.AgentId == agentId);
    }

    public async Task<int> GetTotalDocumentsAsync(int agentId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.AgentDocuments.CountAsync(d => d.AgentId == agentId);
    }

    #endregion

    #region Channel-Agent Assignments

    public async Task<List<ChannelAgentAssignment>> GetChannelAssignmentsAsync(ChannelType channelType)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ChannelAgentAssignments
            .Include(a => a.Agent)
            .Where(a => a.ChannelType == channelType && a.IsEnabled)
            .OrderByDescending(a => a.Priority)
            .ToListAsync();
    }

    public async Task<List<Agent>> GetAgentsForChannelAsync(ChannelType channelType)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var assignments = await db.ChannelAgentAssignments
            .Include(a => a.Agent)
            .Where(a => a.ChannelType == channelType && a.IsEnabled)
            .OrderByDescending(a => a.Priority)
            .ToListAsync();

        return assignments
            .Where(a => a.Agent != null && a.Agent.IsEnabled)
            .Select(a => a.Agent!)
            .ToList();
    }

    public async Task AssignAgentToChannelAsync(int agentId, ChannelType channelType, int priority = 0)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Check if already assigned
        var existing = await db.ChannelAgentAssignments
            .FirstOrDefaultAsync(a => a.AgentId == agentId && a.ChannelType == channelType);

        if (existing != null)
        {
            existing.Priority = priority;
            existing.IsEnabled = true;
        }
        else
        {
            db.ChannelAgentAssignments.Add(new ChannelAgentAssignment
            {
                AgentId = agentId,
                ChannelType = channelType,
                Priority = priority,
                IsEnabled = true,
                AssignedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();

        var agent = await db.Agents.FindAsync(agentId);
        _debugService?.Success("AgentService", $"Assigned agent '{agent?.Name}' to {channelType}");
    }

    public async Task UnassignAgentFromChannelAsync(int agentId, ChannelType channelType)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var assignment = await db.ChannelAgentAssignments
            .FirstOrDefaultAsync(a => a.AgentId == agentId && a.ChannelType == channelType);

        if (assignment != null)
        {
            db.ChannelAgentAssignments.Remove(assignment);
            await db.SaveChangesAsync();

            var agent = await db.Agents.FindAsync(agentId);
            _debugService?.Info("AgentService", $"Unassigned agent '{agent?.Name}' from {channelType}");
        }
    }

    public async Task<List<AgentSearchResult>> SearchChannelKnowledgeAsync(ChannelType channelType, string query, int maxResults = 10, float minSimilarity = 0.15f)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await using var db = await _dbFactory.CreateDbContextAsync();

        _debugService?.Info("VectorSearch", $"Starting search for: {query.Substring(0, Math.Min(50, query.Length))}...");

        // Get agents assigned to this channel
        var assignments = await db.ChannelAgentAssignments
            .Include(a => a.Agent)
            .Where(a => a.ChannelType == channelType && a.IsEnabled)
            .OrderByDescending(a => a.Priority)
            .ToListAsync();
        var dbQueryTime = stopwatch.ElapsedMilliseconds;

        var agentIds = assignments
            .Where(a => a.Agent != null && a.Agent.IsEnabled)
            .Select(a => a.AgentId)
            .ToList();

        if (agentIds.Count == 0)
        {
            _debugService?.Warning("VectorSearch", $"No agents assigned to {channelType}");
            return [];
        }

        // Get agent names for results
        var agentNames = assignments
            .Where(a => a.Agent != null)
            .ToDictionary(a => a.AgentId, a => a.Agent!.Name);

        _debugService?.Info("VectorSearch", $"Found {agentIds.Count} agent(s)",
            $"Agents: {string.Join(", ", agentNames.Values)}");

        // Get query embedding
        stopwatch.Restart();
        var queryEmbedding = await _memoryService.GetEmbeddingAsync(query);
        var embeddingTime = stopwatch.ElapsedMilliseconds;

        if (queryEmbedding.Length == 0)
        {
            _debugService?.Error("VectorSearch", "Failed to generate query embedding");
            return [];
        }

        _debugService?.Info("VectorSearch", $"Query embedding generated",
            $"Time: {embeddingTime}ms | Dimensions: {queryEmbedding.Length}");

        // Get all chunks from assigned agents
        stopwatch.Restart();
        var chunks = await db.AgentChunks
            .Where(c => agentIds.Contains(c.AgentId))
            .ToListAsync();
        var chunkLoadTime = stopwatch.ElapsedMilliseconds;
        _debugService?.Info("VectorSearch", $"Loaded {chunks.Count} chunks",
            $"Time: {chunkLoadTime}ms");

        if (chunks.Count == 0)
        {
            _debugService?.Warning("VectorSearch", "No knowledge chunks found in assigned agents",
                "Make sure documents are uploaded and processed for assigned agents");
            return [];
        }

        // Calculate similarities and rank ALL chunks first
        stopwatch.Restart();
        var allResults = chunks
            .Select(chunk => new AgentSearchResult
            {
                Chunk = chunk,
                Similarity = CosineSimilarity(queryEmbedding, chunk.Embedding),
                DocumentName = GetDocumentName(chunk.Metadata),
                AgentId = chunk.AgentId,
                AgentName = agentNames.GetValueOrDefault(chunk.AgentId, "Unknown")
            })
            .OrderByDescending(r => r.Similarity)
            .ToList();

        // Log similarity distribution
        if (allResults.Count > 0)
        {
            var maxSim = allResults[0].Similarity;
            var minSim = allResults[^1].Similarity;
            var avgSim = allResults.Average(r => r.Similarity);
            _debugService?.Info("VectorSearch",
                $"Similarity distribution: Max={maxSim:F3}, Min={minSim:F3}, Avg={avgSim:F3}");
        }

        // Filter by threshold
        var results = allResults
            .Where(r => r.Similarity > minSimilarity)
            .Take(maxResults)
            .ToList();

        // If no results above threshold but we have chunks, return top ones with lower threshold
        if (results.Count == 0 && allResults.Count > 0)
        {
            _debugService?.Warning("VectorSearch",
                $"No chunks above threshold ({minSimilarity}), returning top {Math.Min(5, allResults.Count)} results");
            results = allResults.Take(Math.Min(5, allResults.Count)).ToList();
        }

        var similarityTime = stopwatch.ElapsedMilliseconds;

        var totalTime = dbQueryTime + embeddingTime + chunkLoadTime + similarityTime;
        _debugService?.Success("VectorSearch",
            $"Search complete: {results.Count}/{chunks.Count} chunks matched",
            $"Total: {totalTime}ms | Embedding: {embeddingTime}ms | Similarity: {similarityTime}ms" +
            (results.Count > 0 ? $" | Best: {results[0].Similarity:F3}" : ""));

        // Log top result preview
        if (results.Count > 0)
        {
            var preview = results[0].Chunk.Content;
            if (preview.Length > 150) preview = preview[..150] + "...";
            _debugService?.Info("VectorSearch", "Top result preview", preview);
        }

        return results;
    }

    #endregion

    #region Helper Methods

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0;

        float dotProduct = 0;
        float normA = 0;
        float normB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator > 0 ? dotProduct / denominator : 0;
    }

    private static string GetDocumentName(string? metadata)
    {
        if (string.IsNullOrEmpty(metadata))
            return "Unknown";

        try
        {
            var json = JsonDocument.Parse(metadata);
            if (json.RootElement.TryGetProperty("fileName", out var fileName))
            {
                return fileName.GetString() ?? "Unknown";
            }
        }
        catch { /* Ignore */ }

        return "Unknown";
    }

    #endregion
}
