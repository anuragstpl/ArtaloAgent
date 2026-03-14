using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArtaloBot.Core.Interfaces;
using ArtaloBot.Core.Models;
using ArtaloBot.Services.LLM;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ExcelDataReader;
using UglyToad.PdfPig;

namespace ArtaloBot.Services.Agents;

/// <summary>
/// Processes documents by extracting text, chunking, and generating embeddings.
/// Supports both simple sentence-based and LLM-based semantic chunking.
/// </summary>
public class DocumentProcessor : IDocumentProcessor
{
    private readonly IMemoryService _memoryService;
    private readonly IDebugService? _debugService;
    private readonly ILLMServiceFactory? _llmFactory;

    private static readonly string[] _supportedExtensions =
    [
        ".txt", ".md", ".csv", ".json",
        ".pdf",
        ".doc", ".docx",
        ".xls", ".xlsx"
    ];

    public IReadOnlyList<string> SupportedExtensions => _supportedExtensions;

    /// <summary>
    /// When true, uses LLM-based semantic chunking for better context preservation.
    /// </summary>
    public bool UseSemanticChunking { get; set; } = true;

    static DocumentProcessor()
    {
        // Required for ExcelDataReader
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public DocumentProcessor(
        IMemoryService memoryService,
        IDebugService? debugService = null,
        ILLMServiceFactory? llmFactory = null)
    {
        _memoryService = memoryService;
        _debugService = debugService;
        _llmFactory = llmFactory;
    }

    public bool IsSupported(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return _supportedExtensions.Contains(ext);
    }

    public async Task<string> ExtractTextAsync(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        _debugService?.Info("DocumentProcessor", $"Extracting text from {Path.GetFileName(filePath)}", $"Type: {ext}");

        return ext switch
        {
            ".txt" or ".md" => await File.ReadAllTextAsync(filePath),
            ".csv" => await ExtractCsvAsync(filePath),
            ".json" => await ExtractJsonAsync(filePath),
            ".pdf" => ExtractPdf(filePath),
            ".doc" or ".docx" => ExtractWordDocument(filePath),
            ".xls" or ".xlsx" => ExtractExcel(filePath),
            _ => throw new NotSupportedException($"File type {ext} is not supported")
        };
    }

    public List<DocumentChunk> ChunkText(string text, int chunkSize = 500, int overlap = 50)
    {
        var chunks = new List<DocumentChunk>();

        if (string.IsNullOrWhiteSpace(text))
            return chunks;

        // Normalize whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();

        // Split by sentences first for better context preservation
        var sentences = SplitIntoSentences(text);
        var currentChunk = new StringBuilder();
        var currentStart = 0;
        var chunkIndex = 0;

        foreach (var sentence in sentences)
        {
            if (currentChunk.Length + sentence.Length > chunkSize && currentChunk.Length > 0)
            {
                // Save current chunk
                chunks.Add(new DocumentChunk
                {
                    Content = currentChunk.ToString().Trim(),
                    Index = chunkIndex++,
                    StartPosition = currentStart,
                    EndPosition = currentStart + currentChunk.Length
                });

                // Start new chunk with overlap
                var overlapText = GetOverlapText(currentChunk.ToString(), overlap);
                currentStart = currentStart + currentChunk.Length - overlapText.Length;
                currentChunk.Clear();
                currentChunk.Append(overlapText);
            }

            currentChunk.Append(sentence);
            currentChunk.Append(' ');
        }

        // Add remaining text
        if (currentChunk.Length > 0)
        {
            chunks.Add(new DocumentChunk
            {
                Content = currentChunk.ToString().Trim(),
                Index = chunkIndex,
                StartPosition = currentStart,
                EndPosition = currentStart + currentChunk.Length
            });
        }

        _debugService?.Info("DocumentProcessor", $"Text chunked into {chunks.Count} parts",
            $"Avg size: {chunks.Average(c => c.Content.Length):F0} chars");

        return chunks;
    }

    /// <summary>
    /// Uses LLM to semantically chunk text, preserving context and topic coherence.
    /// </summary>
    public async Task<List<DocumentChunk>> SemanticChunkTextAsync(
        string text,
        int targetChunkSize = 500,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<DocumentChunk>();

        if (string.IsNullOrWhiteSpace(text))
            return chunks;

        _debugService?.Info("DocumentProcessor", "Starting semantic chunking with LLM");

        // First, split into paragraphs/sections as base units
        var paragraphs = SplitIntoParagraphs(text);
        _debugService?.Info("DocumentProcessor", $"Split into {paragraphs.Count} paragraphs");

        // If document is small enough, just return it as one chunk
        if (text.Length <= targetChunkSize * 1.5)
        {
            chunks.Add(new DocumentChunk
            {
                Content = text.Trim(),
                Index = 0,
                StartPosition = 0,
                EndPosition = text.Length
            });
            return chunks;
        }

        // Try LLM-based chunking first
        if (_llmFactory != null)
        {
            try
            {
                var llmChunks = await ChunkWithLLMAsync(paragraphs, targetChunkSize, cancellationToken);
                if (llmChunks.Count > 0)
                {
                    _debugService?.Success("DocumentProcessor",
                        $"LLM semantic chunking complete: {llmChunks.Count} chunks");
                    return llmChunks;
                }
            }
            catch (Exception ex)
            {
                _debugService?.Warning("DocumentProcessor",
                    $"LLM chunking failed, falling back to rule-based: {ex.Message}");
            }
        }

        // Fallback: Use improved rule-based semantic chunking
        return SemanticChunkByRules(paragraphs, targetChunkSize);
    }

    /// <summary>
    /// Uses LLM to identify natural topic boundaries and group related paragraphs.
    /// </summary>
    private async Task<List<DocumentChunk>> ChunkWithLLMAsync(
        List<string> paragraphs,
        int targetChunkSize,
        CancellationToken cancellationToken)
    {
        var chunks = new List<DocumentChunk>();
        var service = _llmFactory?.GetService(LLMProviderType.Ollama);

        if (service == null)
        {
            _llmFactory?.CreateService(LLMProviderType.Ollama, "", "http://localhost:11434");
            service = _llmFactory?.GetService(LLMProviderType.Ollama);
        }

        if (service == null)
            return chunks;

        // Process in batches to avoid token limits
        var batchSize = 10;
        var chunkIndex = 0;
        var position = 0;

        for (int i = 0; i < paragraphs.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = paragraphs.Skip(i).Take(batchSize).ToList();
            var numberedParagraphs = batch.Select((p, idx) => $"[{i + idx + 1}] {p}").ToList();
            var batchText = string.Join("\n\n", numberedParagraphs);

            // Skip LLM call for very small batches
            if (batchText.Length < targetChunkSize)
            {
                var content = string.Join("\n\n", batch);
                chunks.Add(new DocumentChunk
                {
                    Content = content.Trim(),
                    Index = chunkIndex++,
                    StartPosition = position,
                    EndPosition = position + content.Length,
                    Metadata = new Dictionary<string, string> { ["method"] = "batch_small" }
                });
                position += content.Length;
                continue;
            }

            var prompt = $@"You are a document chunking assistant. Analyze these numbered paragraphs and group them into semantic chunks based on topic coherence.

Paragraphs:
{batchText}

Instructions:
1. Group paragraphs that discuss the same topic or concept together
2. Each group should be roughly {targetChunkSize / 2}-{targetChunkSize} characters when combined
3. Don't split a paragraph - keep each paragraph number in exactly one group
4. Output ONLY the groupings in this exact format, one per line:
GROUP: 1,2,3
GROUP: 4,5
GROUP: 6,7,8,9

Start your response with GROUP:";

            var messages = new List<ChatMessage>
            {
                new() { Role = MessageRole.User, Content = prompt }
            };

            var settings = new LLMSettings
            {
                Model = "qwen2.5:3b",
                Temperature = 0.1f,
                MaxTokens = 500
            };

            var response = await service.SendMessageAsync(messages, settings);

            // Parse the LLM response to get groupings
            var groups = ParseGroupings(response, batch.Count, i);

            foreach (var group in groups)
            {
                var groupParagraphs = group.Select(idx => paragraphs[idx]).ToList();
                var content = string.Join("\n\n", groupParagraphs);

                chunks.Add(new DocumentChunk
                {
                    Content = content.Trim(),
                    Index = chunkIndex++,
                    StartPosition = position,
                    EndPosition = position + content.Length,
                    Metadata = new Dictionary<string, string>
                    {
                        ["method"] = "llm_semantic",
                        ["paragraphs"] = string.Join(",", group.Select(g => g + 1))
                    }
                });
                position += content.Length;
            }
        }

        return chunks;
    }

    /// <summary>
    /// Parses LLM grouping output into paragraph index groups.
    /// </summary>
    private static List<List<int>> ParseGroupings(string response, int batchSize, int offset)
    {
        var groups = new List<List<int>>();
        var usedIndices = new HashSet<int>();

        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (!line.Trim().StartsWith("GROUP:", StringComparison.OrdinalIgnoreCase))
                continue;

            var numbersPart = line.Substring(line.IndexOf(':') + 1).Trim();
            var numbers = Regex.Matches(numbersPart, @"\d+")
                .Select(m => int.TryParse(m.Value, out var n) ? n - 1 : -1) // Convert to 0-based
                .Where(n => n >= 0 && n < offset + batchSize && n >= offset)
                .Distinct()
                .ToList();

            if (numbers.Count > 0)
            {
                var validNumbers = numbers.Where(n => !usedIndices.Contains(n)).ToList();
                if (validNumbers.Count > 0)
                {
                    groups.Add(validNumbers);
                    foreach (var n in validNumbers)
                        usedIndices.Add(n);
                }
            }
        }

        // Add any paragraphs not included in groups
        for (int i = offset; i < offset + batchSize; i++)
        {
            if (!usedIndices.Contains(i))
            {
                groups.Add(new List<int> { i });
            }
        }

        return groups;
    }

    /// <summary>
    /// Improved rule-based semantic chunking that respects document structure.
    /// </summary>
    private List<DocumentChunk> SemanticChunkByRules(List<string> paragraphs, int targetChunkSize)
    {
        var chunks = new List<DocumentChunk>();
        var currentChunk = new StringBuilder();
        var currentParagraphs = new List<string>();
        var chunkIndex = 0;
        var position = 0;

        foreach (var para in paragraphs)
        {
            var isHeading = IsLikelyHeading(para);
            var currentLength = currentChunk.Length;

            // Start new chunk if:
            // 1. Adding this paragraph would exceed target size significantly
            // 2. This is a heading and we have content
            var shouldSplit = (currentLength + para.Length > targetChunkSize * 1.2 && currentLength > targetChunkSize * 0.3)
                           || (isHeading && currentLength > 100);

            if (shouldSplit && currentChunk.Length > 0)
            {
                chunks.Add(new DocumentChunk
                {
                    Content = currentChunk.ToString().Trim(),
                    Index = chunkIndex++,
                    StartPosition = position,
                    EndPosition = position + currentChunk.Length,
                    Metadata = new Dictionary<string, string> { ["method"] = "rule_semantic" }
                });
                position += currentChunk.Length;
                currentChunk.Clear();
                currentParagraphs.Clear();
            }

            if (currentChunk.Length > 0)
                currentChunk.AppendLine();

            currentChunk.Append(para);
            currentParagraphs.Add(para);
        }

        // Add remaining content
        if (currentChunk.Length > 0)
        {
            chunks.Add(new DocumentChunk
            {
                Content = currentChunk.ToString().Trim(),
                Index = chunkIndex,
                StartPosition = position,
                EndPosition = position + currentChunk.Length,
                Metadata = new Dictionary<string, string> { ["method"] = "rule_semantic" }
            });
        }

        _debugService?.Info("DocumentProcessor",
            $"Rule-based semantic chunking: {chunks.Count} chunks",
            $"Avg size: {(chunks.Count > 0 ? chunks.Average(c => c.Content.Length) : 0):F0} chars");

        return chunks;
    }

    /// <summary>
    /// Splits text into paragraphs while preserving structure.
    /// </summary>
    private static List<string> SplitIntoParagraphs(string text)
    {
        var paragraphs = new List<string>();

        // Split by double newlines or multiple newlines
        var parts = Regex.Split(text, @"\n\s*\n");

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                paragraphs.Add(trimmed);
            }
        }

        // If no paragraphs found, split by single newlines for structured data
        if (paragraphs.Count <= 1 && text.Length > 500)
        {
            paragraphs.Clear();
            var lines = text.Split('\n');
            var currentPara = new StringBuilder();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine))
                {
                    if (currentPara.Length > 0)
                    {
                        paragraphs.Add(currentPara.ToString().Trim());
                        currentPara.Clear();
                    }
                }
                else
                {
                    if (currentPara.Length > 0)
                        currentPara.AppendLine();
                    currentPara.Append(trimmedLine);
                }
            }

            if (currentPara.Length > 0)
                paragraphs.Add(currentPara.ToString().Trim());
        }

        return paragraphs;
    }

    /// <summary>
    /// Detects if a paragraph is likely a heading/title.
    /// </summary>
    private static bool IsLikelyHeading(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Short text without ending punctuation
        if (text.Length < 100 && !text.EndsWith('.') && !text.EndsWith(','))
        {
            // Starts with number or bullet
            if (Regex.IsMatch(text, @"^(\d+[\.\):]|[-*•]|\#{1,6})\s"))
                return true;

            // All caps or title case with no lowercase
            if (text.ToUpper() == text && text.Length > 3)
                return true;

            // Contains common heading words
            if (Regex.IsMatch(text, @"^(Chapter|Section|Part|Introduction|Conclusion|Summary|Overview|Appendix)", RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }

    public async Task<List<AgentChunk>> ProcessDocumentAsync(
        int agentId,
        int documentId,
        string filePath,
        IProgress<DocumentProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<AgentChunk>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        _debugService?.Info("DocumentProcessor", $"Processing document: {Path.GetFileName(filePath)}",
            $"Semantic chunking: {(UseSemanticChunking ? "enabled" : "disabled")}");

        // Extract text
        progress?.Report(new DocumentProcessingProgress { Stage = "Extracting text", CurrentChunk = 0, TotalChunks = 1 });
        var extractStart = stopwatch.ElapsedMilliseconds;
        var text = await ExtractTextAsync(filePath);
        var extractTime = stopwatch.ElapsedMilliseconds - extractStart;

        if (string.IsNullOrWhiteSpace(text))
        {
            _debugService?.Warning("DocumentProcessor", "No text content found in document");
            return result;
        }

        _debugService?.Info("DocumentProcessor", $"Text extracted: {text.Length} chars",
            $"Time: {extractTime}ms");

        // Chunk text - use semantic chunking if enabled
        progress?.Report(new DocumentProcessingProgress { Stage = "Chunking text", CurrentChunk = 0, TotalChunks = 1 });
        var chunkStart = stopwatch.ElapsedMilliseconds;

        List<DocumentChunk> chunks;
        if (UseSemanticChunking)
        {
            _debugService?.Info("DocumentProcessor", "Using semantic (LLM-based) chunking...");
            chunks = await SemanticChunkTextAsync(text, targetChunkSize: 600, cancellationToken);
        }
        else
        {
            chunks = ChunkText(text, chunkSize: 500, overlap: 50);
        }
        var chunkTime = stopwatch.ElapsedMilliseconds - chunkStart;

        if (chunks.Count == 0)
        {
            _debugService?.Warning("DocumentProcessor", "No chunks generated from document");
            return result;
        }

        _debugService?.Info("DocumentProcessor", $"Chunking complete: {chunks.Count} chunks",
            $"Time: {chunkTime}ms | Method: {(UseSemanticChunking ? "semantic" : "sentence-based")}");

        // Generate embeddings for each chunk
        var embedStart = stopwatch.ElapsedMilliseconds;
        _debugService?.Info("DocumentProcessor", $"Generating embeddings for {chunks.Count} chunks...");

        for (int i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new DocumentProcessingProgress
            {
                Stage = "Generating embeddings",
                CurrentChunk = i + 1,
                TotalChunks = chunks.Count
            });

            var chunk = chunks[i];
            var embedding = await _memoryService.GetEmbeddingAsync(chunk.Content);

            var metadata = new Dictionary<string, object>
            {
                ["startPosition"] = chunk.StartPosition,
                ["endPosition"] = chunk.EndPosition,
                ["fileName"] = Path.GetFileName(filePath),
                ["chunkMethod"] = UseSemanticChunking ? "semantic" : "sentence"
            };

            // Include any chunk metadata from semantic chunking
            foreach (var kv in chunk.Metadata)
            {
                metadata[kv.Key] = kv.Value;
            }

            result.Add(new AgentChunk
            {
                AgentId = agentId,
                DocumentId = documentId,
                Content = chunk.Content,
                Embedding = embedding,
                EmbeddingModel = "nomic-embed-text", // TODO: Get from settings
                ChunkIndex = chunk.Index,
                Metadata = JsonSerializer.Serialize(metadata),
                CreatedAt = DateTime.UtcNow
            });
        }

        var embedTime = stopwatch.ElapsedMilliseconds - embedStart;
        var totalTime = stopwatch.ElapsedMilliseconds;

        _debugService?.Success("DocumentProcessor",
            $"Document processed: {result.Count} chunks",
            $"Total: {totalTime}ms | Extract: {extractTime}ms | Chunk: {chunkTime}ms | Embed: {embedTime}ms");

        return result;
    }

    #region Text Extraction Methods

    private static async Task<string> ExtractCsvAsync(string filePath)
    {
        var sb = new StringBuilder();
        var lines = await File.ReadAllLinesAsync(filePath);

        // Get headers
        if (lines.Length > 0)
        {
            var headers = ParseCsvLine(lines[0]);

            // Convert each row to readable text
            for (int i = 1; i < lines.Length; i++)
            {
                var values = ParseCsvLine(lines[i]);
                var rowText = new List<string>();

                for (int j = 0; j < Math.Min(headers.Length, values.Length); j++)
                {
                    if (!string.IsNullOrWhiteSpace(values[j]))
                    {
                        rowText.Add($"{headers[j]}: {values[j]}");
                    }
                }

                if (rowText.Count > 0)
                {
                    sb.AppendLine(string.Join(", ", rowText));
                }
            }
        }

        return sb.ToString();
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString().Trim());
        return result.ToArray();
    }

    private static async Task<string> ExtractJsonAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);

        try
        {
            using var doc = JsonDocument.Parse(json);
            return FlattenJson(doc.RootElement);
        }
        catch
        {
            // Return raw JSON if parsing fails
            return json;
        }
    }

    private static string FlattenJson(JsonElement element, string prefix = "")
    {
        var sb = new StringBuilder();

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var newPrefix = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                    sb.Append(FlattenJson(prop.Value, newPrefix));
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    sb.Append(FlattenJson(item, $"{prefix}[{index++}]"));
                }
                break;

            default:
                if (!string.IsNullOrEmpty(prefix))
                {
                    sb.AppendLine($"{prefix}: {element}");
                }
                break;
        }

        return sb.ToString();
    }

    private static string ExtractPdf(string filePath)
    {
        var sb = new StringBuilder();

        using var document = PdfDocument.Open(filePath);
        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
        }

        return sb.ToString();
    }

    private static string ExtractWordDocument(string filePath)
    {
        var sb = new StringBuilder();

        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document?.Body;

        if (body != null)
        {
            foreach (var element in body.Elements())
            {
                if (element is Paragraph para)
                {
                    sb.AppendLine(para.InnerText);
                }
                else if (element is Table table)
                {
                    foreach (var row in table.Elements<TableRow>())
                    {
                        var cells = row.Elements<TableCell>()
                            .Select(c => c.InnerText)
                            .ToArray();
                        sb.AppendLine(string.Join(" | ", cells));
                    }
                }
            }
        }

        return sb.ToString();
    }

    private static string ExtractExcel(string filePath)
    {
        var sb = new StringBuilder();

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        var result = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration
            {
                UseHeaderRow = true
            }
        });

        foreach (DataTable table in result.Tables)
        {
            sb.AppendLine($"Sheet: {table.TableName}");

            var columns = table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray();

            foreach (DataRow row in table.Rows)
            {
                var rowText = new List<string>();
                for (int i = 0; i < columns.Length; i++)
                {
                    var value = row[i]?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        rowText.Add($"{columns[i]}: {value}");
                    }
                }

                if (rowText.Count > 0)
                {
                    sb.AppendLine(string.Join(", ", rowText));
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    #endregion

    #region Helper Methods

    private static List<string> SplitIntoSentences(string text)
    {
        // Simple sentence splitting - can be improved with NLP
        var sentences = new List<string>();
        var pattern = @"(?<=[.!?])\s+";

        var parts = Regex.Split(text, pattern);
        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                sentences.Add(part.Trim());
            }
        }

        return sentences;
    }

    private static string GetOverlapText(string text, int overlapChars)
    {
        if (text.Length <= overlapChars)
            return text;

        // Try to find a sentence boundary near the overlap point
        var start = text.Length - overlapChars;
        var sentenceStart = text.LastIndexOf(". ", start);

        if (sentenceStart > start - 100 && sentenceStart < text.Length - 10)
        {
            return text[(sentenceStart + 2)..];
        }

        // Fall back to word boundary
        var wordStart = text.LastIndexOf(' ', start);
        if (wordStart > 0)
        {
            return text[(wordStart + 1)..];
        }

        return text[start..];
    }

    #endregion
}
