using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private InferenceSession? _session;
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
            if (_session == null) return Result<float[]>.Failure("Embedding model not loaded.");

            // Basic tokenization (whitespace/punctuation) - ideally should be BERT tokenization
            // For now, we'll use a simplified version for the PoC
            var tokens = Tokenize(text);
            var inputIds = tokens.Select(t => (long)t).ToArray();
            
            // BERT models usually expect [batch, seq_len]
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
            
            // The output of BGE models is usually the first element of the last hidden state (CLS token)
            // or a pooled output. Let's assume 'last_hidden_state' and take the CLS token (index 0).
            var outputTensor = results.First().AsTensor<float>();
            var embedding = new float[outputTensor.Dimensions[2]];
            
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] = outputTensor[0, 0, i]; // Batch 0, Token 0 (CLS), Feature i
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
        var models = await _modelRegistry.GetAvailableModelsAsync().ConfigureAwait(false);
        var manifest = models.FirstOrDefault(m => m.Type == AIModelType.Embedding);
        
        if (manifest == null || manifest.Id == _currentModelId) return;

        _session?.Dispose();
        var modelPath = Path.Combine(manifest.LocalPath, manifest.Metadata.GetValueOrDefault("FileName", "model.onnx"));
        
        if (File.Exists(modelPath))
        {
            _session = new InferenceSession(modelPath);
            _currentModelId = manifest.Id;
            _logger.Info("OnnxEmbeddingService", $"Loaded embedding model: {manifest.DisplayName}");
        }
    }

    private int[] Tokenize(string text)
    {
        // IMPROVED: Basic whitespace/punctuation tokenization. 
        // This is still a placeholder as proper BERT tokenization requires a WordPiece vocab.
        // However, this avoids the character-code issue.
        var words = text.ToLowerInvariant()
            .Split(new[] { ' ', '.', ',', '!', '?', ';', ':', '-', '(', ')', '[', ']', '\"', '\'' }, StringSplitOptions.RemoveEmptyEntries);
        
        var tokens = new List<int>();
        tokens.Add(101); // [CLS] placeholder

        foreach (var word in words.Take(500))
        {
            // Simple hash-based mapping for PoC until a real vocab is loaded
            int hash = Math.Abs(word.GetHashCode()) % 30000;
            tokens.Add(hash);
        }

        tokens.Add(102); // [SEP] placeholder
        return tokens.ToArray();
    }

    private float[] Normalize(float[] vector)
    {
        float norm = MathF.Sqrt(vector.Sum(x => x * x));
        return vector.Select(x => x / norm).ToArray();
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
