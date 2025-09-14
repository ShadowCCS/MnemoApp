using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MnemoApp.Core.AI.Models;

namespace MnemoApp.Core.AI.Drivers
{
    /// <summary>
    /// Driver for OpenAI-compatible API endpoints (Ollama, vLLM, etc.)
    /// </summary>
    public class OpenAICompatibleDriver : IModelDriver
    {
        public string DriverName => "OpenAICompatibleDriver";

        private readonly HttpClient _httpClient;
        private readonly Services.IAILogger _logger;

        public OpenAICompatibleDriver(Services.IAILogger? logger = null)
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
            _logger = logger ?? new Services.DebugAILogger();
        }

        public bool CanHandle(AIModel model)
        {
            return model.Capabilities?.OpenAiCompatible == true ||
                   model.Capabilities?.HttpEndpoint?.Contains("/v1/chat/completions") == true ||
                   model.Capabilities?.ExecutionConfig?.ContainsKey("openaiCompatible") == true;
        }

        public async Task<bool> InitializeAsync(AIModel model)
        {
            if (model.Capabilities?.HttpEndpoint == null)
            {
                _logger.LogError($"OpenAI driver init failed for '{model.Manifest.Name}': No HTTP endpoint configured");
                return false;
            }

            // Set API key if provided
            if (!string.IsNullOrEmpty(model.Capabilities.ApiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", model.Capabilities.ApiKey);
            }

            try
            {
                var success = await TestConnectionAsync(model.Capabilities.HttpEndpoint);
                if (!success)
                {
                    _logger.LogWarning($"OpenAI driver init failed for '{model.Manifest.Name}': Connection test failed to {model.Capabilities.HttpEndpoint}");
                }
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError($"OpenAI driver init failed for '{model.Manifest.Name}': {ex.GetType().Name}: {ex.Message} (endpoint: {model.Capabilities.HttpEndpoint})", ex);
                return false;
            }
        }

        public async Task<AIInferenceResponse> InferAsync(AIModel model, AIInferenceRequest request, CancellationToken cancellationToken = default)
        {
            if (model.Capabilities?.HttpEndpoint == null)
            {
                return new AIInferenceResponse
                {
                    Success = false,
                    ErrorMessage = "No HTTP endpoint configured"
                };
            }

            try
            {
                var startTime = DateTime.UtcNow;
                var messages = BuildMessageList(model, request);

                var payload = new
                {
                    model = model.Capabilities?.ExecutionConfig?.GetValueOrDefault("modelName", model.Manifest.OriginName) ?? model.Manifest.OriginName,
                    messages = messages,
                    temperature = request.Temperature,
                    top_p = request.TopP,
                    max_tokens = request.MaxTokens,
                    stop = request.StopTokens ?? model.Capabilities?.StopTokens,
                    stream = false
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(model.Capabilities.HttpEndpoint, content, cancellationToken);
                response.EnsureSuccessStatusCode();

                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<JsonElement>(responseText);

                // Robust parsing for different provider response formats
                var generatedText = ExtractGeneratedText(result);
                var (tokensGenerated, tokensProcessed) = ExtractTokenUsage(result, generatedText, request.Prompt);

                var processingTime = DateTime.UtcNow - startTime;

                return new AIInferenceResponse
                {
                    Success = true,
                    Response = CleanResponse(generatedText, model, request),
                    TokensGenerated = tokensGenerated,
                    TokensProcessed = tokensProcessed,
                    ProcessingTime = processingTime,
                    Metadata = new Dictionary<string, object>
                    {
                        ["model"] = model.Manifest.Name,
                        ["driver"] = DriverName,
                        ["endpoint"] = model.Capabilities.HttpEndpoint
                    }
                };
            }
            catch (Exception ex)
            {
                return new AIInferenceResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async IAsyncEnumerable<string> InferStreamAsync(AIModel model, AIInferenceRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (model.Capabilities?.HttpEndpoint == null)
            {
                yield return "Error: No HTTP endpoint configured";
                yield break;
            }

            var messages = BuildMessageList(model, request);

            var payload = new
            {
                model = model.Capabilities?.ExecutionConfig?.GetValueOrDefault("modelName", model.Manifest.OriginName) ?? model.Manifest.OriginName,
                messages = messages,
                temperature = request.Temperature,
                top_p = request.TopP,
                max_tokens = request.MaxTokens,
                stop = request.StopTokens ?? model.Capabilities?.StopTokens,
                stream = true
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, model.Capabilities.HttpEndpoint)
            {
                Content = content
            };
            var response = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line))
                    continue;

                // Handle SSE format variations
                string jsonData;
                if (line.StartsWith("data: "))
                {
                    jsonData = line[6..]; // Remove "data: " prefix
                }
                else if (line.StartsWith("data:"))
                {
                    jsonData = line[5..]; // Some providers don't include space
                }
                else if (line.StartsWith("{")) // Direct JSON without SSE wrapper
                {
                    jsonData = line;
                }
                else
                {
                    continue; // Skip non-data lines (heartbeats, comments, etc.)
                }

                jsonData = jsonData.Trim();
                if (jsonData == "[DONE]" || jsonData == "")
                    break;

                string? token = null;
                try
                {
                    var streamResult = JsonSerializer.Deserialize<JsonElement>(jsonData);
                    token = ExtractStreamingToken(streamResult);
                }
                catch (Exception ex)
                {
                    // Log but continue - some providers send malformed chunks
                    _logger.LogWarning($"Failed to parse streaming chunk: {ex.Message}, data: {jsonData}");
                    continue;
                }

                if (!string.IsNullOrEmpty(token))
                {
                    // Clean each streaming token to remove stop tokens
                    var cleanedToken = CleanStreamingToken(token, model, request);
                    if (!string.IsNullOrEmpty(cleanedToken))
                        yield return cleanedToken;
                }
            }
        }

        public Task ShutdownAsync(AIModel model)
        {
            // No cleanup needed for HTTP-only driver
            return Task.CompletedTask;
        }

        public async Task<bool> IsReadyAsync(AIModel model)
        {
            if (model.Capabilities?.HttpEndpoint == null)
                return false;

            return await TestConnectionAsync(model.Capabilities.HttpEndpoint);
        }

        private List<object> BuildMessageList(AIModel model, AIInferenceRequest request)
        {
            var messages = new List<object>();

            // Add system message if supported
            if (!string.IsNullOrEmpty(request.SystemPrompt) && model.Capabilities?.SystemPromptSupport == true)
            {
                messages.Add(new { role = "system", content = request.SystemPrompt });
            }

            // Add conversation history if provided
            if (request.ConversationHistory != null && model.Capabilities?.MultiTurnSupport == true)
            {
                for (int i = 0; i < request.ConversationHistory.Count; i++)
                {
                    var role = i % 2 == 0 ? "user" : "assistant";
                    messages.Add(new { role = role, content = request.ConversationHistory[i] });
                }
            }

            // Add thinking prompt if enabled
            var userContent = request.Prompt;
            if (model.Capabilities?.SupportsThinking == true && !string.IsNullOrEmpty(model.Capabilities.ThinkingPrompt))
            {
                userContent = model.Capabilities.ThinkingPrompt + "\n\n" + userContent;
            }

            // Add current user message
            messages.Add(new { role = "user", content = userContent });

            return messages;
        }

        private async Task<bool> TestConnectionAsync(string endpoint)
        {
            try
            {
                // Try to get models list or send a simple request
                var testPayload = new
                {
                    model = "test",
                    messages = new[] { new { role = "user", content = "test" } },
                    max_tokens = 1
                };

                var json = JsonSerializer.Serialize(testPayload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await _httpClient.PostAsync(endpoint, content, cts.Token);
                
                // Accept both success and model-not-found errors as "connection working"
                return response.IsSuccessStatusCode || 
                       (int)response.StatusCode == 404 || 
                       (int)response.StatusCode == 422;
            }
            catch
            {
                return false;
            }
        }

        private string ExtractGeneratedText(JsonElement result)
        {
            try
            {
                // Try OpenAI format first
                if (result.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("message", out var message) && 
                        message.TryGetProperty("content", out var content))
                    {
                        return content.GetString() ?? "";
                    }
                    // Try alternative formats (some providers use "text" instead of "message.content")
                    if (choice.TryGetProperty("text", out var text))
                    {
                        return text.GetString() ?? "";
                    }
                }
                
                // Fallback for other formats
                if (result.TryGetProperty("text", out var directText))
                {
                    return directText.GetString() ?? "";
                }
                if (result.TryGetProperty("content", out var directContent))
                {
                    return directContent.GetString() ?? "";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to extract generated text: {ex.Message}");
            }
            
            return "";
        }

        private (int tokensGenerated, int tokensProcessed) ExtractTokenUsage(JsonElement result, string generatedText, string promptText)
        {
            try
            {
                if (result.TryGetProperty("usage", out var usage))
                {
                    var completion = usage.TryGetProperty("completion_tokens", out var compTokens) ? compTokens.GetInt32() : 0;
                    var prompt = usage.TryGetProperty("prompt_tokens", out var promptTokens) ? promptTokens.GetInt32() : 0;
                    
                    if (completion > 0 || prompt > 0)
                        return (completion, prompt);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to extract token usage: {ex.Message}");
            }
            
            // Fallback to estimation when usage not provided
            var estimatedGenerated = EstimateTokenCount(generatedText);
            var estimatedPrompt = EstimateTokenCount(promptText);
            return (estimatedGenerated, estimatedPrompt);
        }

        private string? ExtractStreamingToken(JsonElement streamResult)
        {
            try
            {
                // OpenAI format: choices[0].delta.content
                if (streamResult.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("delta", out var delta))
                    {
                        if (delta.TryGetProperty("content", out var content))
                            return content.GetString();
                        if (delta.TryGetProperty("text", out var text))
                            return text.GetString();
                    }
                    // Some providers use message.content in streaming
                    if (choice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var msgContent))
                        return msgContent.GetString();
                }
                
                // Alternative formats: direct content/text properties
                if (streamResult.TryGetProperty("content", out var directContent))
                    return directContent.GetString();
                if (streamResult.TryGetProperty("text", out var directText))
                    return directText.GetString();
                if (streamResult.TryGetProperty("token", out var token))
                    return token.GetString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to extract streaming token: {ex.Message}");
            }
            
            return null;
        }

        private string CleanStreamingToken(string token, AIModel model, AIInferenceRequest request)
        {
            if (string.IsNullOrEmpty(token))
                return token;

            // For streaming, we need to be more careful - only remove complete stop tokens
            var commonStopTokens = new[]
            {
                "<|im_end|>", "|<im_end|>", "<im_end>", "im_end>",
                "<|endoftext|>", "<|end_of_text|>",
                "</s>", "<s>",
                "<|assistant|>", "<|user|>", "<|system|>",
                "### Human:", "### Assistant:", "Human:", "Assistant:",
                "[INST]", "[/INST]", "<<SYS>>", "<</SYS>>",
                "<end_of_turn>", "<start_of_turn>",
            };

            var stopTokens = request.StopTokens ?? model.Capabilities?.StopTokens ?? new List<string>();
            var allStopTokens = stopTokens.Concat(commonStopTokens).Distinct().ToList();

            // For streaming tokens, only remove if the token contains a complete stop token
            foreach (var stopToken in allStopTokens.OrderByDescending(t => t.Length))
            {
                if (token.Contains(stopToken, StringComparison.OrdinalIgnoreCase))
                {
                    // Return empty string to stop the stream
                    return "";
                }
            }

            return token;
        }

        private string CleanResponse(string text, AIModel model, AIInferenceRequest request)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var cleaned = text.Trim();

            // Remove common instruction-tuned model stop tokens
            var commonStopTokens = new[]
            {
                "<|im_end|>", "|<im_end|>", "<im_end>", "im_end>", // ChatML format variants
                "<|endoftext|>", "<|end_of_text|>", // GPT variants
                "</s>", "<s>", // Llama variants
                "<|assistant|>", "<|user|>", "<|system|>", // Role tokens
                "### Human:", "### Assistant:", "Human:", "Assistant:", // Alpaca variants
                "[INST]", "[/INST]", "<<SYS>>", "<</SYS>>", // Llama-2 chat variants
                "<end_of_turn>", "<start_of_turn>", // Gemma variants
            };

            // First try model-specific stop tokens
            var stopTokens = request.StopTokens ?? model.Capabilities?.StopTokens ?? new List<string>();
            
            // Add common stop tokens to the list
            var allStopTokens = stopTokens.Concat(commonStopTokens).Distinct().ToList();

            // Remove any stop tokens that appear at the end
            foreach (var stopToken in allStopTokens.OrderByDescending(t => t.Length))
            {
                if (cleaned.EndsWith(stopToken, StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned.Substring(0, cleaned.Length - stopToken.Length).TrimEnd();
                }
            }

            // Also remove stop tokens that appear anywhere (some models put them mid-response)
            foreach (var stopToken in allStopTokens.OrderByDescending(t => t.Length))
            {
                cleaned = cleaned.Replace(stopToken, "", StringComparison.OrdinalIgnoreCase);
            }

            return cleaned.Trim();
        }

        private int EstimateTokenCount(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            // Basic heuristic: words * 1.3 for subword tokenization
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return Math.Max(1, (int)(words * 1.3));
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
