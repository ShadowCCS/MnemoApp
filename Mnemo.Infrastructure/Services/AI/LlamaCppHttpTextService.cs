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

            await _serverManager.EnsureRunningAsync(manifest, ct).ConfigureAwait(false);
            using (_serverManager.BeginRequest(manifest.Id))
            {
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
        [EnumeratorCancellation] CancellationToken ct,
        IReadOnlyList<string>? imageBase64Contents = null)
    {
        if (string.IsNullOrEmpty(manifest.Endpoint))
        {
            _logger.Error("LlamaCppHttpTextService", $"Model {manifest.DisplayName} has no endpoint configured.");
            yield break;
        }

        try
        {
            await _serverManager.EnsureRunningAsync(manifest, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("LlamaCppHttpTextService", $"Failed to start server for {manifest.DisplayName}", ex);
            yield break;
        }

        using (_serverManager.BeginRequest(manifest.Id))
        {
            await foreach (var chunk in GetStreamingChunksAsync(manifest, prompt, imageBase64Contents, ct))
            {
                yield return chunk;
            }
        }
    }

    private async IAsyncEnumerable<string> GetStreamingChunksAsync(
        AIModelManifest manifest,
        string prompt,
        IReadOnlyList<string>? imageBase64Contents,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var endpoint = $"{manifest.Endpoint!.TrimEnd('/')}/v1/chat/completions";
        var (temperature, maxTokens) = GetGenerationParams(manifest);
        var requestBody = BuildChatRequest(prompt, temperature, maxTokens, stream: true, manifest, responseJsonSchema: null, imageBase64Contents);

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
            // Small buffer so we receive SSE lines (and thus tokens) as soon as the server sends them
            reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 128);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("data: "))
                {
                    var jsonData = line["data: ".Length..];

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

    /// <summary>Build request body for chat completions. When responseJsonSchema is set, adds response_format so the Llama server forces JSON output. When imageBase64Contents is set, builds multimodal user content (OpenAI vision format).</summary>
    private static object BuildChatRequest(string prompt, double temperature, int maxTokens, bool stream, AIModelManifest manifest, object? responseJsonSchema = null, IReadOnlyList<string>? imageBase64Contents = null)
    {
        object content;
        if (imageBase64Contents != null && imageBase64Contents.Count > 0)
        {
            var parts = new List<object> { new { type = "text", text = prompt } };
            foreach (var base64 in imageBase64Contents)
            {
                var url = string.IsNullOrEmpty(base64) ? "data:image/png;base64," : $"data:image/png;base64,{base64.Trim()}";
                parts.Add(new { type = "image_url", image_url = new { url } });
            }
            content = parts;
        }
        else
        {
            content = prompt;
        }

        var messages = new[] { new { role = "user", content } };
        object? responseFormat = null;

        if (responseJsonSchema != null)
        {
            var schemaName = manifest.Role == AIModelRoles.Manager ? "routing" : "learning_path";
            responseFormat = BuildJsonSchemaResponseFormat(schemaName, responseJsonSchema);
        }

        return responseFormat != null
            ? new { messages, temperature, max_tokens = maxTokens, stream, response_format = responseFormat }
            : new { messages, temperature, max_tokens = maxTokens, stream };
    }

    /// <summary>Build response_format for Llama server forced JSON output (OpenAI-compatible json_schema). Same pattern used for manager routing and learning path.</summary>
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

    private (double temperature, int maxTokens) GetGenerationParams(AIModelManifest manifest)
    {
        // Mini orchestration model: deterministic, bounded JSON
        if (manifest.Role == AIModelRoles.Manager)
        {
            return (0.0, 256);
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
