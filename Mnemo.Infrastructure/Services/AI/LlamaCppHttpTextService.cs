using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Text generation service that communicates with llama.cpp server via OpenAI-compatible HTTP API.
/// Integrates with LlamaCppServerManager to ensure servers are running before requests.
/// </summary>
public class LlamaCppHttpTextService : ITextGenerationService
{
    private readonly HttpClient _httpClient;
    private readonly ILoggerService _logger;
    private readonly ISettingsService _settings;
    private readonly LlamaCppServerManager _serverManager;

    public LlamaCppHttpTextService(ILoggerService logger, ISettingsService settings, LlamaCppServerManager serverManager)
    {
        _logger = logger;
        _settings = settings;
        _serverManager = serverManager;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    public async Task<Result<string>> GenerateAsync(AIModelManifest manifest, string prompt, CancellationToken ct, object? responseJsonSchema = null)
    {
        try
        {
            if (string.IsNullOrEmpty(manifest.Endpoint))
            {
                return Result<string>.Failure($"Model {manifest.DisplayName} has no endpoint configured.");
            }

            // Ensure server is running
            await _serverManager.EnsureRunningAsync(manifest, ct).ConfigureAwait(false);

            var endpoint = $"{manifest.Endpoint.TrimEnd('/')}/v1/chat/completions";
            
            var (temperature, maxTokens) = GetGenerationParams(manifest);

            object requestBody = BuildChatRequest(prompt, temperature, maxTokens, stream: false, manifest, responseJsonSchema);

            _logger.Info("LlamaCppHttpTextService", $"Sending request to {endpoint} (temp={temperature}, max_tokens={maxTokens})");

            var response = await _httpClient.PostAsJsonAsync(endpoint, requestBody, ct).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return Result<string>.Failure($"HTTP {response.StatusCode}: {errorBody}");
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
            
            if (!json.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                return Result<string>.Failure("Response did not contain choices array.");
            }

            var messageContent = choices[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return Result<string>.Success(messageContent ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.Error("LlamaCppHttpTextService", $"Failed to generate text with {manifest.DisplayName}", ex);
            return Result<string>.Failure($"Generation failed: {ex.Message}", ex);
        }
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        AIModelManifest manifest, 
        string prompt, 
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrEmpty(manifest.Endpoint))
        {
            _logger.Error("LlamaCppHttpTextService", $"Model {manifest.DisplayName} has no endpoint configured.");
            yield break;
        }

        // Ensure server is running
        try
        {
            await _serverManager.EnsureRunningAsync(manifest, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("LlamaCppHttpTextService", $"Failed to start server for {manifest.DisplayName}", ex);
            yield break;
        }

        var endpoint = $"{manifest.Endpoint.TrimEnd('/')}/v1/chat/completions";
        var (temperature, maxTokens) = GetGenerationParams(manifest);
        var requestBody = BuildChatRequest(prompt, temperature, maxTokens, stream: true, manifest, responseJsonSchema: null);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        _logger.Info("LlamaCppHttpTextService", $"Streaming request to {endpoint} (temp={temperature}, max_tokens={maxTokens})");

        HttpResponseMessage? response = null;
        Stream? stream = null;
        StreamReader? reader = null;

        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.Error("LlamaCppHttpTextService", $"Streaming failed with status {response.StatusCode}: {errorBody}");
                yield break;
            }

            stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            reader = new StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("data: "))
                {
                    var jsonData = line["data: ".Length..];
                    
                    // Check for [DONE] marker
                    if (jsonData.Trim() == "[DONE]")
                    {
                        break;
                    }

                    JsonElement json;
                    if (!TryParseJson(jsonData, out json))
                    {
                        _logger.Warning("LlamaCppHttpTextService", $"Failed to parse SSE data: {jsonData}");
                        continue;
                    }
                    
                    if (json.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var delta = choices[0].GetProperty("delta");
                        if (delta.TryGetProperty("content", out var content))
                        {
                            var text = content.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                yield return text;
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            reader?.Dispose();
            stream?.Dispose();
            response?.Dispose();
        }
    }

    private bool TryParseJson(string jsonData, out JsonElement element)
    {
        try
        {
            element = JsonSerializer.Deserialize<JsonElement>(jsonData);
            return true;
        }
        catch (JsonException)
        {
            element = default;
            return false;
        }
    }

    public void UnloadModel(string modelId)
    {
        // Unloading is handled by LlamaCppServerManager's idle timeout system
        _logger.Info("LlamaCppHttpTextService", $"UnloadModel called for {modelId} (managed by ServerManager)");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    /// <summary>Build request body for chat completions. When responseJsonSchema is set or manifest is a router, adds response_format so the Llama server forces JSON output (same mechanism as routing).</summary>
    private static object BuildChatRequest(string prompt, double temperature, int maxTokens, bool stream, AIModelManifest manifest, object? responseJsonSchema = null)
    {
        var messages = new[] { new { role = "user", content = prompt } };
        object? responseFormat = null;

        if (responseJsonSchema != null)
        {
            responseFormat = BuildJsonSchemaResponseFormat("learning_path", responseJsonSchema);
        }
        else if (manifest.Role == "router")
        {
            responseFormat = BuildJsonSchemaResponseFormat("router_response", GetRouterSchema());
        }

        return responseFormat != null
            ? new { messages, temperature, max_tokens = maxTokens, stream, response_format = responseFormat }
            : new { messages, temperature, max_tokens = maxTokens, stream };
    }

    /// <summary>Build response_format for Llama server forced JSON output (OpenAI-compatible json_schema). Same pattern used for router and learning path.</summary>
    private static object BuildJsonSchemaResponseFormat(string schemaName, object schema)
    {
        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = schemaName,
                strict = true,
                schema
            }
        };
    }

    private static readonly object RouterSchema = new
    {
        type = "object",
        properties = new
        {
            r = new
            {
                type = "integer",
                @enum = new[] { 0, 1 },
                description = "0=SMALL fast, 1=LARGE smart"
            }
        },
        required = new[] { "r" },
        additionalProperties = false
    };

    private static object GetRouterSchema() => RouterSchema;

    private (double temperature, int maxTokens) GetGenerationParams(AIModelManifest manifest)
    {
        // Router model needs deterministic, short responses
        if (manifest.Role == "router")
        {
            return (0.0, 10);
        }

        // Try to read from metadata
        var temperature = 0.6;
        var maxTokens = 8192;

        if (manifest.Metadata.TryGetValue("Temperature", out var tempStr) && double.TryParse(tempStr, out var temp))
        {
            temperature = temp;
        }

        if (manifest.Metadata.TryGetValue("MaxTokens", out var maxStr) && int.TryParse(maxStr, out var max))
        {
            maxTokens = max;
        }

        return (temperature, maxTokens);
    }
}
