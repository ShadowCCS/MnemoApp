using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

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

            var readResult = await ReadDocumentContentForIngestAsync(path, ct).ConfigureAwait(false);
            if (!readResult.IsSuccess)
            {
                _logger.Error("KnowledgeService", readResult.ErrorMessage ?? "Document read failed.", readResult.Exception);
                return Result.Failure(readResult.ErrorMessage ?? "Document read failed.", readResult.Exception);
            }

            var content = readResult.Value!;
            var sourceId = Guid.NewGuid().ToString();

            var chunkTexts = ChunkText(content).ToList();
            if (chunkTexts.Count == 0)
            {
                const string msg =
                    "Document text produced no embeddable chunks (content too short, formatting-only, or chunking removed everything).";
                _logger.Error("KnowledgeService", msg);
                return Result.Failure(msg);
            }

            var embedSw = Stopwatch.StartNew();
            var embeddingBatch = await _embeddingService.GetEmbeddingsBatchAsync(chunkTexts, ct).ConfigureAwait(false);
            embedSw.Stop();
            if (!embeddingBatch.IsSuccess)
            {
                _logger.Error("KnowledgeService",
                    $"Batch embedding failed for {path}: {embeddingBatch.ErrorMessage}", embeddingBatch.Exception);
                return Result.Failure(embeddingBatch.ErrorMessage ?? "Embedding failed.", embeddingBatch.Exception);
            }

            var knowledgeChunks = new List<KnowledgeChunk>(chunkTexts.Count);
            for (var i = 0; i < chunkTexts.Count; i++)
            {
                knowledgeChunks.Add(new KnowledgeChunk
                {
                    Content = chunkTexts[i],
                    SourceId = sourceId,
                    ScopeId = scopeId,
                    Embedding = embeddingBatch.Value![i],
                    Metadata = new Dictionary<string, object> { { "path", path } }
                });
            }

            var saveSw = Stopwatch.StartNew();
            await _vectorStore.SaveChunksAsync(knowledgeChunks, ct).ConfigureAwait(false);
            saveSw.Stop();

            _logger.Info("KnowledgeService",
                $"Finished ingesting document: {path} | embedded {knowledgeChunks.Count}/{chunkTexts.Count} chunks | " +
                $"embed {embedSw.ElapsedMilliseconds} ms, save {saveSw.ElapsedMilliseconds} ms (total {embedSw.ElapsedMilliseconds + saveSw.ElapsedMilliseconds} ms).");

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

    private async Task<Result<string>> ReadDocumentContentForIngestAsync(string path, CancellationToken ct)
    {
        try
        {
            if (string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var pdfText = await Task.Run(() => ReadPdfText(path), ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(pdfText))
                {
                    return Result<string>.Failure(
                        "This PDF has no extractable text in Mnemo (PdfPig). Common causes: scanned image-only pages, " +
                        "text stored only as outlines/paths, or fonts/encodings PdfPig cannot decode. " +
                        "Use a text-based PDF, export plain text from the source, or run OCR and ingest the result.");
                }

                _logger.Info("KnowledgeService", $"Extracted PDF text: {path} ({pdfText.Length} characters).");
                return Result<string>.Success(pdfText);
            }

            var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
                return Result<string>.Failure("Document is empty or whitespace only; nothing to ingest.");

            return Result<string>.Success(text);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Failed to read document: {ex.Message}", ex);
        }
    }

    private static string ReadPdfText(string path)
    {
        using var document = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            var block = ExtractPageText(page);
            if (!string.IsNullOrWhiteSpace(block))
                sb.AppendLine(block);
        }

        return sb.ToString();
    }

    private static string ExtractPageText(Page page)
    {
        var t = page.Text;
        if (!string.IsNullOrWhiteSpace(t))
            return t;

        var words = page.GetWords();
        if (words.Any())
            return string.Join(" ", words.Select(w => w.Text));

        var letters = page.Letters;
        if (letters.Any())
            return string.Join("", letters.Select(l => l.Value));

        return "";
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
