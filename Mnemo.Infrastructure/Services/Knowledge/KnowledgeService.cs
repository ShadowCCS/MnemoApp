using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Knowledge;

public class KnowledgeService : IKnowledgeService
{
    private const int MinChunkChars = 300;
    private const int MaxChunkChars = 1000;

    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILoggerService _logger;

    public KnowledgeService(IVectorStore vectorStore, IEmbeddingService embeddingService, ILoggerService logger)
    {
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<Result> IngestDocumentAsync(string path, string? scopeId = null, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(path)) return Result.Failure("File not found.");

            var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var sourceId = Guid.NewGuid().ToString();
            
            var chunkTexts = ChunkText(content);
            var knowledgeChunks = new System.Collections.Concurrent.ConcurrentBag<KnowledgeChunk>();

            // Parallelize embedding generation with bounded parallelism to avoid CPU/memory contention
            var parallelOptions = new ParallelOptions 
            { 
                CancellationToken = ct,
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) 
            };

            await Parallel.ForEachAsync(chunkTexts, parallelOptions, async (chunkText, token) =>
            {
                var embeddingResult = await _embeddingService.GetEmbeddingAsync(chunkText, token).ConfigureAwait(false);
                if (embeddingResult.IsSuccess)
                {
                    knowledgeChunks.Add(new KnowledgeChunk
                    {
                        Content = chunkText,
                        SourceId = sourceId,
                        ScopeId = scopeId,
                        Embedding = embeddingResult.Value,
                        Metadata = new Dictionary<string, object> { { "path", path } }
                    });
                }
            }).ConfigureAwait(false);

            await _vectorStore.SaveChunksAsync(knowledgeChunks, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.Error("KnowledgeService", $"Failed to ingest document: {path}", ex);
            return Result.Failure($"Failed to ingest document: {path}", ex);
        }
    }

    public async Task<Result<IEnumerable<KnowledgeChunk>>> SearchAsync(string query, int limit = 5, string? scopeId = null, CancellationToken ct = default)
    {
        try
        {
            var embeddingResult = await _embeddingService.GetEmbeddingAsync(query, ct).ConfigureAwait(false);
            if (!embeddingResult.IsSuccess) return Result<IEnumerable<KnowledgeChunk>>.Failure(embeddingResult.ErrorMessage!);

            var results = await _vectorStore.SearchAsync(embeddingResult.Value!, limit, scopeId, ct).ConfigureAwait(false);
            return Result<IEnumerable<KnowledgeChunk>>.Success(results);
        }
        catch (Exception ex)
        {
            _logger.Error("KnowledgeService", "Search failed", ex);
            return Result<IEnumerable<KnowledgeChunk>>.Failure("Search failed", ex);
        }
    }

    public async Task<Result> RemoveSourceAsync(string sourceId, CancellationToken ct = default)
    {
        try
        {
            await _vectorStore.DeleteBySourceAsync(sourceId, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.Error("KnowledgeService", $"Failed to remove source: {sourceId}", ex);
            return Result.Failure($"Failed to remove source: {sourceId}", ex);
        }
    }

    public async Task<Result> RemoveScopeAsync(string scopeId, CancellationToken ct = default)
    {
        try
        {
            await _vectorStore.DeleteByScopeAsync(scopeId, ct).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.Error("KnowledgeService", $"Failed to remove scope: {scopeId}", ex);
            return Result.Failure($"Failed to remove scope: {scopeId}", ex);
        }
    }

    private IEnumerable<string> ChunkText(string text)
    {
        text = NormalizeWhitespace(text);
        var paragraphs = text.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        var rawChunks = new List<string>();

        foreach (var p in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (p.Length <= MaxChunkChars)
            {
                rawChunks.Add(p.Trim());
            }
            else
            {
                var sentences = Regex.Split(p, @"(?<=[.!?])\s+");
                string currentChunk = "";
                foreach (var s in sentences)
                {
                    if (currentChunk.Length + s.Length > MaxChunkChars)
                    {
                        if (!string.IsNullOrEmpty(currentChunk)) rawChunks.Add(currentChunk.Trim());
                        currentChunk = s;
                    }
                    else
                    {
                        currentChunk += (currentChunk.Length > 0 ? " " : "") + s;
                    }
                }
                if (!string.IsNullOrEmpty(currentChunk)) rawChunks.Add(currentChunk.Trim());
            }
        }

        return MergeSmallChunks(rawChunks);
    }

    /// <summary>Normalize whitespace so character counts reflect real content (e.g. fix PDF line breaks and hyphen splits).</summary>
    private static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        text = Regex.Replace(text, "\r\n", "\n");
        text = Regex.Replace(text, "\n{3,}", "\n\n");
        text = Regex.Replace(text, @"-\s*\n\s*", ""); // hyphen at line end -> join with next line
        text = Regex.Replace(text, @"(?<!\n)\n(?!\n)", " ");
        return Regex.Replace(text, " +", " ").Trim();
    }

    /// <summary>Merge chunks smaller than MinChunkChars with the next chunk to reduce embedding count.</summary>
    private static List<string> MergeSmallChunks(List<string> rawChunks)
    {
        var result = new List<string>(rawChunks.Count);
        int i = 0;
        while (i < rawChunks.Count)
        {
            string c = rawChunks[i];
            while (c.Length < MinChunkChars && i + 1 < rawChunks.Count)
            {
                i++;
                c = c + " " + rawChunks[i];
            }
            result.Add(c);
            i++;
        }

        // Last chunk under min: merge backward to avoid one tiny embedding per document
        if (result.Count > 1 && result[result.Count - 1].Length < MinChunkChars)
        {
            result[result.Count - 2] = result[result.Count - 2] + " " + result[result.Count - 1];
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }
}
