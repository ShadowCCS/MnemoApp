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
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILoggerService _logger;

    public KnowledgeService(IVectorStore vectorStore, IEmbeddingService embeddingService, ILoggerService logger)
    {
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<Result> IngestDocumentAsync(string path, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(path)) return Result.Failure("File not found.");

            var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var sourceId = Guid.NewGuid().ToString();
            
            // Simple semantic chunking (by paragraphs/sentences)
            var chunks = ChunkText(content);
            var knowledgeChunks = new List<KnowledgeChunk>();

            foreach (var chunkText in chunks)
            {
                if (ct.IsCancellationRequested) break;

                var embeddingResult = await _embeddingService.GetEmbeddingAsync(chunkText, ct).ConfigureAwait(false);
                if (embeddingResult.IsSuccess)
                {
                    knowledgeChunks.Add(new KnowledgeChunk
                    {
                        Content = chunkText,
                        SourceId = sourceId,
                        Embedding = embeddingResult.Value,
                        Metadata = new Dictionary<string, object> { { "path", path } }
                    });
                }
            }

            await _vectorStore.SaveChunksAsync(knowledgeChunks).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.Error("KnowledgeService", $"Failed to ingest document: {path}", ex);
            return Result.Failure($"Failed to ingest document: {path}", ex);
        }
    }

    public async Task<Result<IEnumerable<KnowledgeChunk>>> SearchAsync(string query, int limit = 5, CancellationToken ct = default)
    {
        try
        {
            var embeddingResult = await _embeddingService.GetEmbeddingAsync(query, ct).ConfigureAwait(false);
            if (!embeddingResult.IsSuccess) return Result<IEnumerable<KnowledgeChunk>>.Failure(embeddingResult.ErrorMessage!);

            var results = await _vectorStore.SearchAsync(embeddingResult.Value!, limit).ConfigureAwait(false);
            return Result<IEnumerable<KnowledgeChunk>>.Success(results);
        }
        catch (Exception ex)
        {
            _logger.Error("KnowledgeService", "Search failed", ex);
            return Result<IEnumerable<KnowledgeChunk>>.Failure("Search failed", ex);
        }
    }

    public async Task<Result> RemoveSourceAsync(string sourceId)
    {
        try
        {
            await _vectorStore.DeleteBySourceAsync(sourceId).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.Error("KnowledgeService", $"Failed to remove source: {sourceId}", ex);
            return Result.Failure($"Failed to remove source: {sourceId}", ex);
        }
    }

    private IEnumerable<string> ChunkText(string text)
    {
        // Split by double newlines (paragraphs) or sentences if paragraphs are too long
        var paragraphs = text.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();

        foreach (var p in paragraphs)
        {
            if (p.Length < 1000)
            {
                chunks.Add(p);
            }
            else
            {
                // Split long paragraphs by sentences
                var sentences = Regex.Split(p, @"(?<=[.!?])\s+");
                string currentChunk = "";
                foreach (var s in sentences)
                {
                    if (currentChunk.Length + s.Length > 1000)
                    {
                        chunks.Add(currentChunk);
                        currentChunk = s;
                    }
                    else
                    {
                        currentChunk += (currentChunk.Length > 0 ? " " : "") + s;
                    }
                }
                if (!string.IsNullOrEmpty(currentChunk)) chunks.Add(currentChunk);
            }
        }

        return chunks;
    }
}
