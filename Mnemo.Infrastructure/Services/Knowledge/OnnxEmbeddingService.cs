using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Knowledge;

public class OnnxEmbeddingService : IEmbeddingService, IDisposable
{
    /// <summary>
    /// Micro-batches balance ORT run overhead vs CPU cache and attention cost. Very large batches (100+ × 512) often
    /// regress wall time on consumer CPUs versus tens of smaller runs.
    /// </summary>
    private const int MaxBatchSize = 24;

    /// <summary>Longest subword we attempt to match; caps WordPiece from O(n²) substring work on pathological tokens.</summary>
    private const int MaxWordPieceSubwordChars = 64;

    private readonly IAIModelRegistry _modelRegistry;
    private readonly ILoggerService _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private InferenceSession? _session;
    private IReadOnlyDictionary<string, int>? _vocab;
    private int _clsTokenId = 101;
    private int _sepTokenId = 102;
    private int _padTokenId;
    private string? _currentModelId;
    private bool _modelWantsTokenTypeIds = true;
    private string _embeddingOutputName = "last_hidden_state";

    public OnnxEmbeddingService(IAIModelRegistry modelRegistry, ILoggerService logger)
    {
        _modelRegistry = modelRegistry;
        _logger = logger;
    }

    public async Task<Result<float[]>> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var batch = await GetEmbeddingsBatchAsync(new[] { text }, ct).ConfigureAwait(false);
        if (!batch.IsSuccess) return Result<float[]>.Failure(batch.ErrorMessage!, batch.Exception);
        return Result<float[]>.Success(batch.Value![0]);
    }

    public async Task<Result<IReadOnlyList<float[]>>> GetEmbeddingsBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts == null || texts.Count == 0)
            return Result<IReadOnlyList<float[]>>.Success(Array.Empty<float[]>());

        try
        {
            await EnsureSessionAsync(ct).ConfigureAwait(false);
            if (_session == null || _vocab == null)
                return Result<IReadOnlyList<float[]>>.Failure("Embedding model or vocab not loaded.");

            var combined = new List<float[]>(texts.Count);
            for (var offset = 0; offset < texts.Count; offset += MaxBatchSize)
            {
                ct.ThrowIfCancellationRequested();
                var count = Math.Min(MaxBatchSize, texts.Count - offset);
                combined.AddRange(RunEmbeddingBatch(texts, offset, count));
            }

            return Result<IReadOnlyList<float[]>>.Success(combined);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("OnnxEmbeddingService", "Failed to generate batch embedding", ex);
            return Result<IReadOnlyList<float[]>>.Failure($"Batch embedding failed: {ex.Message}", ex);
        }
    }

    private List<float[]> RunEmbeddingBatch(IReadOnlyList<string> texts, int offset, int count)
    {
        const int maxSeqLen = 512;
        var rows = new int[count][];
        var maxLen = 0;
        for (var i = 0; i < count; i++)
        {
            var ids = Tokenize(texts[offset + i]);
            if (ids.Length > maxSeqLen)
                ids = ids.AsSpan(0, maxSeqLen).ToArray();
            rows[i] = ids;
            if (ids.Length > maxLen) maxLen = ids.Length;
        }

        var batch = count;
        var inputIdsBuf = new long[batch * maxLen];
        var attentionMaskBuf = new long[batch * maxLen];
        var tokenTypeBuf = _modelWantsTokenTypeIds ? new long[batch * maxLen] : null;
        for (var b = 0; b < batch; b++)
        {
            var ids = rows[b];
            for (var j = 0; j < maxLen; j++)
            {
                var idx = b * maxLen + j;
                if (j < ids.Length)
                {
                    inputIdsBuf[idx] = ids[j];
                    attentionMaskBuf[idx] = 1;
                }
                else
                {
                    inputIdsBuf[idx] = _padTokenId;
                    attentionMaskBuf[idx] = 0;
                }

                if (tokenTypeBuf != null)
                    tokenTypeBuf[idx] = 0;
            }
        }

        var inputTensor = new DenseTensor<long>(inputIdsBuf, new[] { batch, maxLen });
        var attentionMask = new DenseTensor<long>(attentionMaskBuf, new[] { batch, maxLen });

        var inputs = new List<NamedOnnxValue>(3)
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask)
        };
        if (_modelWantsTokenTypeIds && tokenTypeBuf != null)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids",
                new DenseTensor<long>(tokenTypeBuf, new[] { batch, maxLen })));
        }

        using var results = _session!.Run(inputs);

        var outputValue = results.FirstOrDefault(r => r.Name == _embeddingOutputName)
            ?? results.FirstOrDefault(r => r.Name == "last_hidden_state")
            ?? results.First();
        var outputTensor = outputValue.AsTensor<float>();

        var embeddings = new List<float[]>(batch);
        var rank = outputTensor.Dimensions.Length;
        if (rank == 2)
        {
            var dim = (int)outputTensor.Dimensions[1];
            for (var b = 0; b < batch; b++)
            {
                var embedding = new float[dim];
                for (var i = 0; i < dim; i++) embedding[i] = outputTensor[b, i];
                embeddings.Add(Normalize(embedding));
            }
        }
        else if (rank == 3)
        {
            var dim = (int)outputTensor.Dimensions[2];
            for (var b = 0; b < batch; b++)
            {
                var embedding = new float[dim];
                for (var i = 0; i < dim; i++) embedding[i] = outputTensor[b, 0, i];
                embeddings.Add(Normalize(embedding));
            }
        }
        else if (batch == 1)
        {
            embeddings.Add(Normalize(outputTensor.ToArray()));
        }
        else
        {
            throw new InvalidOperationException(
                "Expected ONNX output rank 2 [batch, dim] or rank 3 [batch, seq, dim] for batched embedding.");
        }

        return embeddings;
    }

    private static SessionOptions CreateSessionOptions()
    {
        var logical = Environment.ProcessorCount;
        // Matmul-heavy models on hyper-threaded CPUs often regress if intra-op uses every logical CPU.
        var intra = logical <= 4 ? logical : logical / 2;
        intra = Math.Clamp(intra, 1, 8);

        return new SessionOptions
        {
            InterOpNumThreads = 1,
            IntraOpNumThreads = intra,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
    }

    private static void ApplyModelIoMetadata(InferenceSession session, out bool wantsTokenTypeIds, out string embeddingOutputName)
    {
        wantsTokenTypeIds = session.InputMetadata.ContainsKey("token_type_ids");
        embeddingOutputName = ResolveEmbeddingOutputName(session);
    }

    private static string ResolveEmbeddingOutputName(InferenceSession session)
    {
        ReadOnlySpan<string> preferred = ["sentence_embedding", "pooler_output", "last_hidden_state"];
        foreach (var name in preferred)
        {
            if (session.OutputMetadata.ContainsKey(name))
                return name;
        }

        foreach (var key in session.OutputMetadata.Keys)
            return key;

        return "last_hidden_state";
    }

    private void WarmupEmbeddingRun(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            _ = RunEmbeddingBatch(new[] { "." }, 0, 1);
        }
        catch (Exception ex)
        {
            _logger.Warning("OnnxEmbeddingService", $"Embedding warmup run failed (non-fatal): {ex.Message}");
        }
    }

    private async Task EnsureSessionAsync(CancellationToken ct)
    {
        // Quick check without lock - if already loaded, skip
        if (_session != null && _vocab != null && _currentModelId != null)
        {
            return;
        }

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            var models = await _modelRegistry.GetAvailableModelsAsync().ConfigureAwait(false);
            var manifest = models.FirstOrDefault(m => m.Type == AIModelType.Embedding);

            if (manifest == null) return;
            // Do not skip when _session is missing (dispose/failed init); only skip if this model is already loaded.
            if (_session != null && manifest.Id == _currentModelId) return;

            _session?.Dispose();
            var modelPath = Path.Combine(manifest.LocalPath, manifest.Metadata.GetValueOrDefault("FileName", "model.onnx"));
            var tokenizerPath = Path.Combine(manifest.LocalPath, "tokenizer.json");
            
            if (File.Exists(modelPath) && File.Exists(tokenizerPath))
            {
                try
                {
                    var session = new InferenceSession(modelPath, CreateSessionOptions());
                    
                    var json = await File.ReadAllTextAsync(tokenizerPath, ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    
                    if (doc.RootElement.TryGetProperty("model", out var model) && 
                        model.TryGetProperty("vocab", out var vocabElement))
                    {
                        var newVocab = new Dictionary<string, int>();
                        foreach (var prop in vocabElement.EnumerateObject())
                        {
                            newVocab[prop.Name] = prop.Value.GetInt32();
                        }

                        // Attempt to find special tokens from config files if available
                        var configPath = Path.Combine(manifest.LocalPath, "tokenizer_config.json");
                        var clsTokenName = "[CLS]";
                        var sepTokenName = "[SEP]";

                        var padTokenName = "";
                        if (File.Exists(configPath))
                        {
                            try
                            {
                                var configJson = await File.ReadAllTextAsync(configPath, ct).ConfigureAwait(false);
                                using var configDoc = JsonDocument.Parse(configJson);
                                
                                if (configDoc.RootElement.TryGetProperty("cls_token", out var cls)) 
                                    clsTokenName = cls.ValueKind == JsonValueKind.String ? cls.GetString()! : cls.GetProperty("content").GetString()!;
                                    
                                if (configDoc.RootElement.TryGetProperty("sep_token", out var sep)) 
                                    sepTokenName = sep.ValueKind == JsonValueKind.String ? sep.GetString()! : sep.GetProperty("content").GetString()!;

                                if (configDoc.RootElement.TryGetProperty("pad_token", out var padTok))
                                    padTokenName = padTok.ValueKind == JsonValueKind.String ? padTok.GetString()! : padTok.GetProperty("content").GetString()!;
                            }
                            catch { /* Fallback to defaults */ }
                        }

                        ApplyModelIoMetadata(session, out var wantTokenTypeIds, out var embeddingOut);
                        _modelWantsTokenTypeIds = wantTokenTypeIds;
                        _embeddingOutputName = embeddingOut;

                        // Atomically set all fields after successful initialization
                        _clsTokenId = newVocab.GetValueOrDefault(clsTokenName, 101);
                        _sepTokenId = newVocab.GetValueOrDefault(sepTokenName, 102);
                        _padTokenId = newVocab.GetValueOrDefault(padTokenName, 0);
                        _vocab = newVocab;
                        _session = session;
                        _currentModelId = manifest.Id;

                        WarmupEmbeddingRun(ct);

                        _logger.Info("OnnxEmbeddingService",
                            $"Loaded embedding model and vocab: {manifest.DisplayName} ({_vocab.Count} tokens, CLS: {clsTokenName}, SEP: {sepTokenName}); " +
                            $"ORT output '{_embeddingOutputName}', token_type_ids={wantTokenTypeIds}.");
                    }
                    else
                    {
                        session.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("OnnxEmbeddingService", $"Failed to load model or vocab from {manifest.LocalPath}", ex);
                    _session = null;
                    _vocab = null;
                    _currentModelId = null;
                }
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private int[] Tokenize(string text)
    {
        if (_vocab == null) return Array.Empty<int>();

        // Normalize: lowercase and split on whitespace
        var normalized = text.ToLowerInvariant();
        
        // Split into tokens, separating punctuation from words
        var tokens = TokenizeWithPunctuation(normalized);
        var ids = new List<int> { _clsTokenId };

        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token)) continue;
            var wordTokens = WordPieceTokenize(token);
            ids.AddRange(wordTokens);
        }

        ids.Add(_sepTokenId);
        return ids.ToArray();
    }

    private static List<string> TokenizeWithPunctuation(string text)
    {
        var result = new List<string>();
        var currentWord = new StringBuilder();

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (currentWord.Length > 0)
                {
                    result.Add(currentWord.ToString());
                    currentWord.Clear();
                }
            }
            else if (char.IsPunctuation(c) || char.IsSymbol(c))
            {
                // Flush current word before punctuation
                if (currentWord.Length > 0)
                {
                    result.Add(currentWord.ToString());
                    currentWord.Clear();
                }
                // Add punctuation as separate token
                result.Add(c.ToString());
            }
            else
            {
                currentWord.Append(c);
            }
        }

        if (currentWord.Length > 0)
        {
            result.Add(currentWord.ToString());
        }

        return result;
    }

    private List<int> WordPieceTokenize(string word)
    {
        var vocab = _vocab!;
        var result = new List<int>();
        var start = 0;
        var unkId = vocab.GetValueOrDefault("[UNK]", 100);

        while (start < word.Length)
        {
            // Longest-match-first, but only consider substrings up to MaxWordPieceSubwordChars (BERT pieces are short).
            var end = Math.Min(word.Length, start + MaxWordPieceSubwordChars);
            var curTokenId = -1;
            var matchedLen = 0;

            while (end > start)
            {
                var len = end - start;
                string substr;
                if (start == 0)
                    substr = word.Substring(start, len);
                else
                {
                    // Single allocation per candidate length (bounded by MaxWordPieceSubwordChars).
                    substr = string.Concat("##", word.AsSpan(start, len));
                }

                if (vocab.TryGetValue(substr, out var id))
                {
                    curTokenId = id;
                    matchedLen = len;
                    break;
                }

                end--;
            }

            if (curTokenId == -1)
            {
                result.Add(unkId);
                start++;
                continue;
            }

            result.Add(curTokenId);
            start += matchedLen;
        }

        return result;
    }

    private float[] Normalize(float[] vector)
    {
        float sum = 0;
        for (int i = 0; i < vector.Length; i++) sum += vector[i] * vector[i];
        float norm = MathF.Sqrt(sum);
        if (norm < 1e-8) return vector;

        var result = new float[vector.Length];
        for (int i = 0; i < vector.Length; i++) result[i] = vector[i] / norm;
        return result;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
        _vocab = null;
        _currentModelId = null;
        _modelWantsTokenTypeIds = true;
        _embeddingOutputName = "last_hidden_state";
        _initLock.Dispose();
    }
}
