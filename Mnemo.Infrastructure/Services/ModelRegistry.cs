using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

public class ModelRegistry : IAIModelRegistry
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Canonical layout: <c>text/manager</c> port 8000, <c>text/low</c> 8001, <c>text/mid</c> 8002, <c>text/high</c> 8003 (GGUF + optional mmproj in the same folder).
    /// </summary>
    private static readonly (string Folder, string Role, int Port)[] TextModelLoadOrder =
    {
        ("manager", AIModelRoles.Manager, 8000),
        ("low", AIModelRoles.Low, 8001),
        ("mid", AIModelRoles.Mid, 8002),
        ("high", AIModelRoles.High, 8003)
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

        var textModelsPath = Path.Combine(_modelsPath, "text");
        var filledRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (folder, role, port) in TextModelLoadOrder)
        {
            if (filledRoles.Contains(role))
                continue;

            var rolePath = Path.Combine(textModelsPath, folder);
            var manifestPath = Path.Combine(rolePath, "manifest.json");
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                var json = await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false);
                var manifest = JsonSerializer.Deserialize<AIModelManifest>(json, _jsonOptions);

                if (manifest != null)
                {
                    manifest.LocalPath = rolePath;
                    manifest.Role = role;

                    if (string.IsNullOrWhiteSpace(manifest.DisplayName))
                        manifest.DisplayName = DefaultDisplayNameForRole(role);

                    manifest.Endpoint = $"http://127.0.0.1:{port}";

                    if (string.IsNullOrEmpty(manifest.PromptTemplate) || manifest.PromptTemplate == "ChatML")
                    {
                        var name = manifest.DisplayName.ToLowerInvariant();
                        if (name.Contains("llama-3") || name.Contains("llama3")) manifest.PromptTemplate = "LLAMA3";
                        else if (name.Contains("alpaca")) manifest.PromptTemplate = "ALPACA";
                        else if (name.Contains("vicuna")) manifest.PromptTemplate = "VICUNA";
                        else if (name.Contains("qwen") || name.Contains("phi-3") || name.Contains("ministral") || name.Contains("chatml")) manifest.PromptTemplate = "CHATML";
                    }

                    _models[manifest.Id] = manifest;
                    filledRoles.Add(role);
                }
            }
            catch (Exception)
            {
                // Log error but continue
            }
        }

        var otherModelDirs = Directory.Exists(_modelsPath)
            ? Directory.GetDirectories(_modelsPath, "*", SearchOption.AllDirectories)
                .Where(d => !d.StartsWith(textModelsPath, StringComparison.OrdinalIgnoreCase))
            : Array.Empty<string>();

        foreach (var dir in otherModelDirs)
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false);
                    var manifest = JsonSerializer.Deserialize<AIModelManifest>(json, _jsonOptions);

                    if (manifest != null && manifest.Type != AIModelType.Text)
                    {
                        manifest.LocalPath = dir;
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

    private static string DefaultDisplayNameForRole(string role) =>
        role switch
        {
            AIModelRoles.Manager => "Manager (orchestration)",
            AIModelRoles.Low => "Low tier",
            AIModelRoles.Mid => "Mid tier",
            AIModelRoles.High => "High tier",
            _ => role
        };
}
