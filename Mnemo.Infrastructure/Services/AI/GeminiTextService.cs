using System;
using System.Collections.Generic;
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

public class GeminiTextService : ITextGenerationService
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settings;
    private readonly ILoggerService _logger;

    public GeminiTextService(ISettingsService settings, ILoggerService logger)
    {
        _settings = settings;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    public async Task<Result<string>> GenerateAsync(AIModelManifest manifest, string prompt, CancellationToken ct)
    {
        try
        {
            var apiKey = await _settings.GetAsync<string>("AI.GeminiApiKey").ConfigureAwait(false);
            if (string.IsNullOrEmpty(apiKey))
            {
                return Result<string>.Failure("Gemini API Key is missing. Please set it in Settings.");
            }

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:generateContent?key={apiKey}";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                }
            };

            var response = await _httpClient.PostAsJsonAsync(url, requestBody, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return Result<string>.Failure($"Gemini API error ({response.StatusCode}): {errorBody}");
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
            var text = json.GetProperty("candidates")[0]
                           .GetProperty("content")
                           .GetProperty("parts")[0]
                           .GetProperty("text")
                           .GetString();

            return Result<string>.Success(text ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.Error("GeminiTextService", "Failed to generate text", ex);
            return Result<string>.Failure($"Gemini generation failed: {ex.Message}");
        }
    }

    public async IAsyncEnumerable<string> GenerateStreamingAsync(AIModelManifest manifest, string prompt, [EnumeratorCancellation] CancellationToken ct)
    {
        var apiKey = await _settings.GetAsync<string>("AI.GeminiApiKey").ConfigureAwait(false);
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.Error("GeminiTextService", "Gemini API Key is missing.");
            yield break;
        }

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:streamGenerateContent?alt=sse&key={apiKey}";

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.Error("GeminiTextService", $"Streaming failed with status {response.StatusCode}: {errorBody}");
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("data: "))
            {
                var jsonData = line["data: ".Length..];
                var json = JsonSerializer.Deserialize<JsonElement>(jsonData);
                
                if (json.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var part = candidates[0].GetProperty("content").GetProperty("parts")[0];
                    if (part.TryGetProperty("text", out var text))
                    {
                        yield return text.GetString() ?? string.Empty;
                    }
                }
            }
        }
    }

    public void UnloadModel(string modelId) { }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

