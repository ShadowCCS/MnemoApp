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
    private readonly IAIModelRegistry _modelRegistry;
    private readonly ILoggerService _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private InferenceSession? _session;
    private IReadOnlyDictionary<string, int>? _vocab;
    private int _clsTokenId = 101;
    private int _sepTokenId = 102;
    private string? _currentModelId;

    public OnnxEmbeddingService(IAIModelRegistry modelRegistry, ILoggerService logger)
    {
        _modelRegistry = modelRegistry;
        _logger = logger;
    }

    public async Task<Result<float[]>> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        try
        {
            await EnsureSessionAsync(ct).ConfigureAwait(false);
            if (_session == null || _vocab == null) return Result<float[]>.Failure("Embedding model or vocab not loaded.");

            var tokens = Tokenize(text);
            var inputIds = tokens.Select(t => (long)t).ToArray();
            
            // Limit sequence length
            if (inputIds.Length > 512) inputIds = inputIds.Take(512).ToArray();

            var inputTensor = new DenseTensor<long>(inputIds, new[] { 1, inputIds.Length });
            var attentionMask = new DenseTensor<long>(Enumerable.Repeat(1L, inputIds.Length).ToArray(), new[] { 1, inputIds.Length });
            var tokenTypeIds = new DenseTensor<long>(Enumerable.Repeat(0L, inputIds.Length).ToArray(), new[] { 1, inputIds.Length });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
                NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
            };

            using var results = _session.Run(inputs);
            
            var outputValue = results.FirstOrDefault(r => r.Name == "last_hidden_state") ?? results.First();
            var outputTensor = outputValue.AsTensor<float>();

            float[] embedding;
            if (outputTensor.Dimensions.Length == 3)
            {
                // [batch, seq, dim] -> take CLS token
                embedding = new float[outputTensor.Dimensions[2]];
                for (int i = 0; i < embedding.Length; i++) embedding[i] = outputTensor[0, 0, i];
            }
            else
            {
                embedding = outputTensor.ToArray();
            }

            return Result<float[]>.Success(Normalize(embedding));
        }
        catch (Exception ex)
        {
            _logger.Error("OnnxEmbeddingService", "Failed to generate embedding", ex);
            return Result<float[]>.Failure($"Embedding failed: {ex.Message}", ex);
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
            
            if (manifest == null || manifest.Id == _currentModelId) return;

            _session?.Dispose();
            var modelPath = Path.Combine(manifest.LocalPath, manifest.Metadata.GetValueOrDefault("FileName", "model.onnx"));
            var tokenizerPath = Path.Combine(manifest.LocalPath, "tokenizer.json");
            
            if (File.Exists(modelPath) && File.Exists(tokenizerPath))
            {
                try
                {
                    var session = new InferenceSession(modelPath);
                    
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
                            }
                            catch { /* Fallback to defaults */ }
                        }

                        // Atomically set all fields after successful initialization
                        _clsTokenId = newVocab.GetValueOrDefault(clsTokenName, 101);
                        _sepTokenId = newVocab.GetValueOrDefault(sepTokenName, 102);
                        _vocab = newVocab;
                        _session = session;
                        _currentModelId = manifest.Id;
                        
                        _logger.Info("OnnxEmbeddingService", $"Loaded embedding model and vocab: {manifest.DisplayName} ({_vocab.Count} tokens, CLS: {clsTokenName}, SEP: {sepTokenName})");
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
        var result = new List<int>();
        var start = 0;
        
        while (start < word.Length)
        {
            var end = word.Length;
            var curTokenId = -1;
            var curTokenStr = "";

            while (start < end)
            {
                var substr = (start == 0) ? word[start..end] : "##" + word[start..end];
                if (_vocab!.TryGetValue(substr, out var id))
                {
                    curTokenId = id;
                    curTokenStr = substr;
                    break;
                }
                end--;
            }

            if (curTokenId == -1)
            {
                // Unknown single character - add [UNK] and continue with next character
                result.Add(_vocab!.GetValueOrDefault("[UNK]", 100));
                start++;
                continue;
            }

            result.Add(curTokenId);
            start += (curTokenStr.StartsWith("##")) ? curTokenStr.Length - 2 : curTokenStr.Length;
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
        _initLock.Dispose();
    }
}
