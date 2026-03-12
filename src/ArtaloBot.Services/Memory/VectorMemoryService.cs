using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using ArtaloBot.Data;
using Microsoft.EntityFrameworkCore;

namespace ArtaloBot.Services.Memory;

/// <summary>
/// Vector memory service that stores conversation messages as embeddings in SQLite,
/// then performs cosine-similarity search to retrieve contextually relevant memories.
/// Supports three embedding backends: Ollama, OpenAI, and a local TF-IDF fallback.
/// </summary>
public class VectorMemoryService : IMemoryService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly IDebugService? _debugService;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public bool IsReady { get; private set; } = true;

    public VectorMemoryService(
        IDbContextFactory<AppDbContext> dbFactory,
        ISettingsService settingsService,
        HttpClient httpClient,
        IDebugService? debugService = null)
    {
        _dbFactory = dbFactory;
        _settingsService = settingsService;
        _httpClient = httpClient;
        _debugService = debugService;
    }

    // ────────────────────────────────────────────────────────────────
    // Public API
    // ────────────────────────────────────────────────────────────────

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetMemorySettingsAsync(cancellationToken);
        var provider = settings.EmbeddingProvider.ToLowerInvariant();

        _debugService?.Info("Embedding", $"Getting embedding using provider: {provider}",
            $"Model: {settings.EmbeddingModel}\nText length: {text.Length} chars");

        var embedding = provider switch
        {
            "ollama" => await GetOllamaEmbeddingAsync(text, settings.EmbeddingModel, cancellationToken),
            "openai" => await GetOpenAIEmbeddingAsync(text, settings.EmbeddingModel, cancellationToken),
            _ => GetSimpleEmbedding(text)
        };

        _debugService?.Success("Embedding", $"Generated {embedding.Length}-dimensional embedding",
            $"Provider: {provider}, First 5 values: [{string.Join(", ", embedding.Take(5).Select(v => v.ToString("F4")))}...]");

        return embedding;
    }

    public async Task StoreMemoryAsync(
        string content,
        MessageRole role,
        int? sessionId = null,
        string tag = "",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            _debugService?.Warning("Memory", "Skipped storing empty content");
            return;
        }

        var settings = await _settingsService.GetMemorySettingsAsync(cancellationToken);
        if (!settings.IsEnabled)
        {
            _debugService?.Info("Memory", "Memory storage disabled, skipping");
            return;
        }

        // Store globally if configured, otherwise use session ID
        var effectiveSessionId = settings.StoreGlobally ? null : sessionId;

        _debugService?.Info("Memory", $"Storing memory for {role}",
            $"Global: {settings.StoreGlobally}, Session: {effectiveSessionId}\nContent: {content.Substring(0, Math.Min(100, content.Length))}...");

        var embedding = await GetEmbeddingAsync(content, cancellationToken);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // Prune oldest entries if over limit (global limit)
        var totalCount = await db.MemoryEntries.CountAsync(cancellationToken);
        if (totalCount >= settings.MaxMemories)
        {
            var deleteCount = totalCount - settings.MaxMemories + 1;
            _debugService?.Info("Memory", $"Pruning {deleteCount} old memories (limit: {settings.MaxMemories})");

            var toDelete = await db.MemoryEntries
                .OrderBy(m => m.CreatedAt)
                .Take(deleteCount)
                .ToListAsync(cancellationToken);
            db.MemoryEntries.RemoveRange(toDelete);
        }

        db.MemoryEntries.Add(new MemoryEntry
        {
            SessionId = effectiveSessionId,
            Role = role,
            Content = content,
            EmbeddingJson = SerializeEmbedding(embedding),
            EmbeddingModel = settings.EmbeddingModel,
            CreatedAt = DateTime.UtcNow,
            Tag = tag
        });

        await db.SaveChangesAsync(cancellationToken);

        var newCount = await db.MemoryEntries.CountAsync(cancellationToken);
        _debugService?.Success("Memory", $"Stored memory (total: {newCount})",
            $"Role: {role}, Global: {settings.StoreGlobally}, Embedding dimensions: {embedding.Length}");
    }

    public async Task<IReadOnlyList<MemorySearchResult>> SearchMemoriesAsync(
        string query,
        int? sessionId = null,
        int topK = 5,
        double? similarityThreshold = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetMemorySettingsAsync(cancellationToken);
        if (!settings.IsEnabled)
        {
            _debugService?.Info("Memory", "Memory search disabled, returning empty results");
            return [];
        }

        var threshold = similarityThreshold ?? settings.SimilarityThreshold;
        var searchGlobally = settings.CrossSessionMemory;

        _debugService?.Info("Memory", $"Searching memories",
            $"CrossSessionMemory: {searchGlobally}, SessionId param: {sessionId}\nTopK: {topK}, Threshold: {threshold:F2}\nQuery: {query.Substring(0, Math.Min(100, query.Length))}...");

        var queryEmbedding = await GetEmbeddingAsync(query, cancellationToken);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // Load all candidate memories
        // Search globally if: CrossSessionMemory is enabled OR sessionId is null
        IQueryable<MemoryEntry> candidatesQuery = db.MemoryEntries;

        bool shouldSearchGlobally = searchGlobally || !sessionId.HasValue;

        if (!shouldSearchGlobally && sessionId.HasValue)
        {
            candidatesQuery = candidatesQuery.Where(m => m.SessionId == sessionId);
            _debugService?.Info("Memory", $"Filtering by session {sessionId}");
        }
        else
        {
            _debugService?.Info("Memory", "Searching ALL memories (global mode)");
        }

        var candidates = await candidatesQuery
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        _debugService?.Info("Memory", $"Found {candidates.Count} candidate memories, computing similarities...");

        // Compute cosine similarity in-memory
        var results = candidates
            .Select(entry =>
            {
                var entryEmbedding = DeserializeEmbedding(entry.EmbeddingJson);
                var score = CosineSimilarity(queryEmbedding, entryEmbedding);
                return new MemorySearchResult(entry, score);
            })
            .Where(r => r.Score >= threshold)
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();

        if (results.Count > 0)
        {
            var details = string.Join("\n", results.Select(r =>
                $"  [{r.Score:F3}] {r.Entry.Role}: {r.Entry.Content.Substring(0, Math.Min(60, r.Entry.Content.Length))}..."));
            _debugService?.Success("Memory", $"Found {results.Count} relevant memories (scores {results.First().Score:F3} - {results.Last().Score:F3})",
                details);
        }
        else
        {
            _debugService?.Info("Memory", $"No memories found above threshold {threshold:F2} (searched {candidates.Count} entries)");
        }

        return results;
    }

    public async Task ClearMemoriesAsync(int? sessionId = null, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entries = await db.MemoryEntries
            .Where(m => m.SessionId == sessionId)
            .ToListAsync(cancellationToken);
        db.MemoryEntries.RemoveRange(entries);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearAllMemoriesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.MemoryEntries.RemoveRange(db.MemoryEntries);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> GetMemoryCountAsync(int? sessionId = null, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        // Always return total count since memories are now stored globally
        return await db.MemoryEntries.CountAsync(cancellationToken);
    }

    // ────────────────────────────────────────────────────────────────
    // Embedding Providers
    // ────────────────────────────────────────────────────────────────

    private async Task<float[]> GetOllamaEmbeddingAsync(
        string text, string model, CancellationToken cancellationToken)
    {
        var settings = await _settingsService.GetMemorySettingsAsync(cancellationToken);
        // Retrieve base URL from Ollama provider settings
        var providerSettings = await _settingsService.GetProviderSettingsAsync(
            ArtaloBot.Core.Models.LLMProviderType.Ollama, cancellationToken);
        var baseUrl = providerSettings?.BaseUrl ?? "http://localhost:11434";

        var embeddingModel = string.IsNullOrEmpty(model) ? "nomic-embed-text" : model;

        _debugService?.Info("Ollama", $"Calling Ollama embedding API",
            $"URL: {baseUrl}/api/embeddings\nModel: {embeddingModel}");

        var requestBody = new { model = embeddingModel, prompt = text };
        var json = JsonSerializer.Serialize(requestBody, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync($"{baseUrl.TrimEnd('/')}/api/embeddings", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<OllamaEmbeddingResponse>(responseJson, JsonOpts);

            if (result?.Embedding != null)
            {
                _debugService?.Success("Ollama", $"Got embedding with {result.Embedding.Length} dimensions");
                return result.Embedding;
            }

            _debugService?.Warning("Ollama", "Ollama returned null embedding, falling back to simple");
            return GetSimpleEmbedding(text);
        }
        catch (Exception ex)
        {
            _debugService?.Error("Ollama", $"Embedding API error: {ex.Message}",
                $"URL: {baseUrl}/api/embeddings\nModel: {embeddingModel}\nException: {ex.GetType().Name}");
            // Fall back to simple embedding
            return GetSimpleEmbedding(text);
        }
    }

    private async Task<float[]> GetOpenAIEmbeddingAsync(
        string text, string model, CancellationToken cancellationToken)
    {
        var providerSettings = await _settingsService.GetProviderSettingsAsync(
            ArtaloBot.Core.Models.LLMProviderType.OpenAI, cancellationToken);
        var apiKey = providerSettings?.ApiKey ?? string.Empty;

        if (string.IsNullOrEmpty(apiKey))
            return GetSimpleEmbedding(text);

        var embeddingModel = string.IsNullOrEmpty(model) ? "text-embedding-3-small" : model;

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var body = new { model = embeddingModel, input = text };
        request.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<OpenAIEmbeddingResponse>(responseJson, JsonOpts);
            return result?.Data?.FirstOrDefault()?.Embedding ?? GetSimpleEmbedding(text);
        }
        catch
        {
            return GetSimpleEmbedding(text);
        }
    }

    /// <summary>
    /// Simple TF-IDF style fallback embedding — no API required.
    /// Creates a sparse bag-of-words vector hashed into 512 buckets.
    /// </summary>
    private static float[] GetSimpleEmbedding(string text)
    {
        const int dimensions = 512;
        var vector = new float[dimensions];

        var words = text.ToLowerInvariant()
            .Split([' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':'],
                StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            // Robert Jenkins hash
            var hash = (uint)word.GetHashCode();
            hash = (hash ^ 61u) ^ (hash >> 16);
            hash += (hash << 3);
            hash ^= (hash >> 4);
            hash *= 0x27D4EB2Du;
            hash ^= (hash >> 15);

            var bucket = (int)(hash % dimensions);
            vector[bucket] += 1f;
        }

        // L2 normalise
        var magnitude = MathF.Sqrt(vector.Sum(v => v * v));
        if (magnitude > 0f)
            for (int i = 0; i < dimensions; i++)
                vector[i] /= magnitude;

        return vector;
    }

    // ────────────────────────────────────────────────────────────────
    // Math helpers
    // ────────────────────────────────────────────────────────────────

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;

        double dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var denom = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denom < 1e-10 ? 0 : dot / denom;
    }

    private static string SerializeEmbedding(float[] embedding)
        => JsonSerializer.Serialize(embedding);

    private static float[] DeserializeEmbedding(string json)
        => JsonSerializer.Deserialize<float[]>(json) ?? [];

    // ────────────────────────────────────────────────────────────────
    // DTOs
    // ────────────────────────────────────────────────────────────────

    private class OllamaEmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }

    private class OpenAIEmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAIEmbeddingData>? Data { get; set; }
    }

    private class OpenAIEmbeddingData
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}
