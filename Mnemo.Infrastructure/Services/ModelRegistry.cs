using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

using System.Text.Json.Serialization;

namespace Mnemo.Infrastructure.Services;

public class ModelRegistry : IAIModelRegistry
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _modelsPath;
    private readonly Dictionary<string, AIModelManifest> _models = new();

    public ModelRegistry()
    {
        _modelsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "mnemo",
            "models");
        
        if (!Directory.Exists(_modelsPath))
        {
            Directory.CreateDirectory(_modelsPath);
        }
    }

    public async Task RefreshAsync()
    {
        _models.Clear();
        var directories = Directory.GetDirectories(_modelsPath, "*", SearchOption.AllDirectories);

        foreach (var dir in directories)
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false);
                    var manifest = JsonSerializer.Deserialize<AIModelManifest>(json, _jsonOptions);

                    if (manifest != null)
                    {
                        manifest.LocalPath = dir;
                        
                        // Set default prompt template if missing
                        if (string.IsNullOrEmpty(manifest.PromptTemplate) || manifest.PromptTemplate == "ChatML")
                        {
                            var name = manifest.DisplayName.ToLowerInvariant();
                            if (name.Contains("llama-3") || name.Contains("llama3")) manifest.PromptTemplate = "LLAMA3";
                            else if (name.Contains("alpaca")) manifest.PromptTemplate = "ALPACA";
                            else if (name.Contains("vicuna")) manifest.PromptTemplate = "VICUNA";
                            else if (name.Contains("qwen") || name.Contains("phi-3") || name.Contains("chatml")) manifest.PromptTemplate = "CHATML";
                        }

                        _models[manifest.Id] = manifest;
                    }
                }
                catch (Exception)
                {
                    // Log error but continue
                }
            }
        }
    }

    public Task<IEnumerable<AIModelManifest>> GetAvailableModelsAsync()
    {
        return Task.FromResult<IEnumerable<AIModelManifest>>(_models.Values);
    }

    public Task<AIModelManifest?> GetModelAsync(string modelId)
    {
        _models.TryGetValue(modelId, out var manifest);
        return Task.FromResult(manifest);
    }
}

