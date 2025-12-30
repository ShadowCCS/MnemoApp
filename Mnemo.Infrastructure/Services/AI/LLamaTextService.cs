using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

public class LLamaTextService : ITextGenerationService
{
    private readonly ILoggerService _logger;
    private readonly IResourceGovernor _governor;
    private readonly ISettingsService _settings;
    private readonly HardwareDetector _hardware;
    private readonly ConcurrentDictionary<string, LLamaWeights> _modelCache = new();

    public LLamaTextService(ILoggerService logger, IResourceGovernor governor, ISettingsService settings, HardwareDetector hardware)
    {
        _logger = logger;
        _governor = governor;
        _settings = settings;
        _hardware = hardware;
        _governor.ModelShouldUnload += UnloadModel;
    }

    public async Task<Result<string>> GenerateAsync(AIModelManifest manifest, string prompt, CancellationToken ct)
    {
        try
        {
            var sb = new StringBuilder();
            await foreach (var token in GenerateStreamingAsync(manifest, prompt, ct).ConfigureAwait(false))
            {
                sb.Append(token);
            }
            return Result<string>.Success(CleanResponse(sb.ToString()));
        }
        catch (Exception ex)
        {
            _logger.Error("LLamaTextService", $"Failed to generate text with {manifest.DisplayName}", ex);
            return Result<string>.Failure($"Generation failed: {ex.Message}", ex);
        }
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(AIModelManifest manifest, string prompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var hardwareInfo = _hardware.Detect();
        var useGpuSetting = await _settings.GetAsync<bool?>("AI.GpuAcceleration").ConfigureAwait(false);
        bool useGpu = useGpuSetting ?? hardwareInfo.HasNvidiaGpu;

        var weights = await GetWeightsAsync(manifest).ConfigureAwait(false);
        var modelPath = ResolveModelPath(manifest);

        var contextSize = manifest.Metadata.TryGetValue("ContextSize", out var csStr) && uint.TryParse(csStr, out var cs)
            ? cs
            : 32768;

        var parameters = new ModelParams(modelPath)
        {
            ContextSize = contextSize,
            GpuLayerCount = useGpu ? 99 : 0,
            Seed = 42,
        };

        var executor = new StatelessExecutor(weights, parameters);

        var pipeline = new LLama.Sampling.DefaultSamplingPipeline();
        pipeline.Temperature = 0.6f;
        pipeline.TopP = 0.9f;
        pipeline.TopK = 40;
        pipeline.RepeatPenalty = 1.1f;

        var inferenceParams = new InferenceParams
        {
            MaxTokens = 8192,
            AntiPrompts = new[]
            {
                "\nUser:", "\nHuman:", "###", "<|", "\n\n\n",
                "### Instruction:", "### Response:", "### Input:",
                "Instruction:", "Input:", "Response:", "User:", "Question:",
                "<|im_start|>", "<|im_end|>", "<|endoftext|>",
                "<|eot_id|>", "<|start_header_id|>", "<|end_header_id|>"
            },
            SamplingPipeline = pipeline
        };

        var totalOutput = new StringBuilder();
        await foreach (var token in executor.InferAsync(prompt, inferenceParams, ct).ConfigureAwait(false))
        {
            totalOutput.Append(token);
            var currentText = totalOutput.ToString();

            if (inferenceParams.AntiPrompts.Any(ap => currentText.EndsWith(ap, StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }

            yield return token;

            if (totalOutput.Length > 32000) break;
        }
    }

    private string ResolveModelPath(AIModelManifest manifest)
    {
        if (!manifest.Metadata.TryGetValue("FileName", out var fileName))
        {
            throw new ArgumentException($"Model manifest for '{manifest.DisplayName}' is missing the 'FileName' metadata field.");
        }

        var modelPath = Path.Combine(manifest.LocalPath, fileName);
        
        if (File.Exists(modelPath)) return modelPath;

        // Try adding .gguf if it's missing
        if (!fileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            var ggufPath = modelPath + ".gguf";
            if (File.Exists(ggufPath)) return ggufPath;
        }

        throw new FileNotFoundException($"Model file not found at: {modelPath}");
    }

    private async Task<LLamaWeights> GetWeightsAsync(AIModelManifest manifest)
    {
        if (_modelCache.TryGetValue(manifest.Id, out var weights))
        {
            return weights;
        }

        var modelPath = ResolveModelPath(manifest);
        
        var hardwareInfo = _hardware.Detect();
        var useGpuSetting = await _settings.GetAsync<bool?>("AI.GpuAcceleration").ConfigureAwait(false);
        var useGpu = useGpuSetting ?? hardwareInfo.HasNvidiaGpu;
        
        var gpuLayers = useGpu ? 99 : 0;
        var parameters = new ModelParams(modelPath)
        {
            GpuLayerCount = gpuLayers
        };
        
        _logger.Info("LLamaTextService", $"Loading model weights from: {modelPath} (GPU: {useGpu})");
        
        try
        {
            var loadedWeights = await Task.Run(() => LLamaWeights.LoadFromFile(parameters)).ConfigureAwait(false);
            _modelCache[manifest.Id] = loadedWeights;
            return loadedWeights;
        }
        catch (Exception ex)
        {
            _logger.Error("LLamaTextService", $"Native error loading model: {ex.Message}", ex);
            throw;
        }
    }

    public void UnloadModel(string modelId)
    {
        if (_modelCache.TryRemove(modelId, out var weights))
        {
            weights.Dispose();
            _logger.Info("LLamaTextService", $"Unloaded model: {modelId}");
        }
    }

    public void Dispose()
    {
        _governor.ModelShouldUnload -= UnloadModel;
        foreach (var weights in _modelCache.Values)
        {
            weights.Dispose();
        }
        _modelCache.Clear();
    }

    private static string CleanResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return string.Empty;

        var result = response.Trim();
        
        // Comprehensive list of markers that might leak into the output
        var markers = new[] 
        { 
            "### System:", "### User:", "### Assistant:", 
            "### Instruction:", "### Response:", "### Input:",
            "<|im_start|>", "<|im_end|>", "<|", 
            "User:", "Human:", "Assistant:", 
            "Question:", "Input:", "Dear AI", "AI Assistant:", "Ai assistant"
        };

        foreach (var marker in markers)
        {
            var idx = result.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                // If it's at index 0, it's repeating the prompt prefix - strip it
                if (idx == 0)
                {
                    result = result[marker.Length..].Trim();
                }
                else
                {
                    // If it's later in the text, it's a hallucinated second turn - cut it
                    result = result[..idx].Trim();
                }
            }
        }
        
        return result;
    }
}
