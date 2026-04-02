using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

public sealed class VertexGeminiTeacherClient : ITeacherModelClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly ISettingsService _settings;
    private readonly ILoggerService _logger;
    private GoogleCredential? _credential;
    private string? _credentialPath;

    public VertexGeminiTeacherClient(ISettingsService settings, ILoggerService logger)
    {
        _settings = settings;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(6) };
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var path = await ResolveCredentialsPathAsync(ct).ConfigureAwait(false);
        return !string.IsNullOrEmpty(path) && File.Exists(path);
    }

    public async Task<Result<string>> GenerateRoutingDecisionJsonAsync(string userBlock, CancellationToken ct = default)
    {
        if (!await IsConfiguredAsync(ct).ConfigureAwait(false))
            return Result<string>.Failure("Vertex credentials are not configured.");

        try
        {
            var token = await GetAccessTokenAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(token))
                return Result<string>.Failure("Could not obtain Google access token.");

            var url = await BuildMethodUrlAsync("generateContent", ct).ConfigureAwait(false);
            var schema = BuildRoutingResponseSchema();
            var (routeTemp, routeMax) = await GetRoutingGenerationParamsAsync(ct).ConfigureAwait(false);

            var body = new
            {
                systemInstruction = new { parts = new[] { new { text = TeacherRoutingPrompts.SystemInstruction } } },
                contents = new object[]
                {
                    new { role = "user", parts = new object[] { new { text = userBlock } } }
                },
                generationConfig = new
                {
                    temperature = routeTemp,
                    maxOutputTokens = routeMax,
                    responseMimeType = "application/json",
                    responseSchema = schema
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning("VertexTeacher", $"Routing generateContent failed: {(int)response.StatusCode} {responseText}");
                return Result<string>.Failure($"Vertex routing failed: HTTP {(int)response.StatusCode}");
            }

            var text = ExtractTextFromGenerateContentResponse(responseText);
            return string.IsNullOrWhiteSpace(text)
                ? Result<string>.Failure("Empty routing response from Gemini.")
                : Result<string>.Success(text.Trim());
        }
        catch (Exception ex)
        {
            _logger.Error("VertexTeacher", "GenerateRoutingDecisionJsonAsync failed", ex);
            return Result<string>.Failure(ex.Message, ex);
        }
    }

    public async IAsyncEnumerable<string> StreamChatAsync(
        string systemPrompt,
        string userMessage,
        IReadOnlyList<string>? imageBase64Contents,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!await IsConfiguredAsync(ct).ConfigureAwait(false))
            yield break;

        var token = await GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
            yield break;

        object[] userParts;
        if (imageBase64Contents != null && imageBase64Contents.Count > 0)
        {
            var parts = new List<object> { new { text = userMessage } };
            foreach (var b64 in imageBase64Contents)
            {
                var data = (b64 ?? "").Trim();
                parts.Add(new { inlineData = new { mimeType = "image/jpeg", data = data } });
            }

            userParts = parts.ToArray();
        }
        else
        {
            userParts = new object[] { new { text = userMessage } };
        }

        object? systemInstruction = null;
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            systemInstruction = new { parts = new object[] { new { text = systemPrompt } } };

        var (chatTemp, chatMax) = await GetChatGenerationParamsAsync(ct).ConfigureAwait(false);

        var body = new
        {
            systemInstruction,
            contents = new object[]
            {
                new { role = "user", parts = userParts }
            },
            generationConfig = new
            {
                temperature = chatTemp,
                maxOutputTokens = chatMax
            }
        };

        var url = await BuildMethodUrlAsync("streamGenerateContent", ct).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        HttpResponseMessage? response = null;
        Stream? stream = null;
        StreamReader? reader = null;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.Warning("VertexTeacher", $"streamGenerateContent failed: {(int)response.StatusCode} {err}");
                yield break;
            }

            stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            reader = new StreamReader(stream, Encoding.UTF8);
            var streamText = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            var yielded = false;
            foreach (var payload in EnumerateVertexStreamPayloadStrings(streamText))
            {
                foreach (var fragment in ExtractStreamTextFragments(payload))
                {
                    yielded = true;
                    yield return fragment;
                }
            }

            if (!yielded && streamText.Length > 0)
            {
                _logger.Warning(
                    "VertexTeacher",
                    $"StreamChatAsync: no text extracted from stream body (len={streamText.Length}, head={TruncateForLog(streamText, 500)})");
            }
        }
        finally
        {
            reader?.Dispose();
            stream?.Dispose();
            response?.Dispose();
        }
    }

    public async IAsyncEnumerable<StreamChunk> StreamChatWithToolsAsync(
        IReadOnlyList<object> messages,
        IReadOnlyList<SkillToolDefinition> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!await IsConfiguredAsync(ct).ConfigureAwait(false))
            yield break;

        var token = await GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
            yield break;

        var (systemInstruction, contents) = GeminiTeacherMessageConverter.Convert(messages);
        if (contents.Count == 0)
            yield break;

        var (chatTemp, chatMax) = await GetChatGenerationParamsAsync(ct).ConfigureAwait(false);
        var gen = new { temperature = chatTemp, maxOutputTokens = chatMax };

        // With tools: use non-streaming generateContent. Vertex/Gemini often omits or splits functionCall
        // across stream chunks; a single response reliably includes complete functionCall parts in content.parts.
        if (tools.Count > 0)
        {
            var decls = tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                parameters = ToolParametersJsonSchema.Normalize(t.Parameters)
            }).ToArray();

            var requestBody = new
            {
                systemInstruction,
                contents,
                tools = new object[] { new { functionDeclarations = decls } },
                toolConfig = new { functionCallingConfig = new { mode = "AUTO" } },
                generationConfig = gen
            };

            var url = await BuildMethodUrlAsync("generateContent", ct).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning("VertexTeacher", $"generateContent (tools) failed: {(int)response.StatusCode} {responseText}");
                yield break;
            }

            var yielded = false;
            foreach (var chunk in EnumerateChunksFromGenerateContentResponse(responseText))
            {
                yielded = true;
                yield return chunk;
            }

            if (!yielded && responseText.Length > 0)
            {
                _logger.Warning(
                    "VertexTeacher",
                    $"StreamChatWithToolsAsync: no text or functionCall parsed from generateContent (len={responseText.Length}, head={TruncateForLog(responseText, 500)})");
            }

            yield break;
        }

        var requestBodyNoTools = new
        {
            systemInstruction,
            contents,
            generationConfig = gen
        };

        var streamUrl = await BuildMethodUrlAsync("streamGenerateContent", ct).ConfigureAwait(false);
        using var streamRequest = new HttpRequestMessage(HttpMethod.Post, streamUrl);
        streamRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        streamRequest.Content = new StringContent(JsonSerializer.Serialize(requestBodyNoTools, JsonOptions), Encoding.UTF8, "application/json");

        HttpResponseMessage? responseStream = null;
        Stream? stream = null;
        StreamReader? reader = null;

        try
        {
            responseStream = await _http.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!responseStream.IsSuccessStatusCode)
            {
                var err = await responseStream.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.Warning("VertexTeacher", $"streamGenerateContent failed: {(int)responseStream.StatusCode} {err}");
                yield break;
            }

            stream = await responseStream.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            reader = new StreamReader(stream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            var yieldedText = false;
            foreach (var payload in EnumerateVertexStreamPayloadStrings(body))
            {
                foreach (var fragment in ExtractStreamTextFragments(payload))
                {
                    yieldedText = true;
                    yield return new StreamChunk.Content(fragment);
                }
            }

            if (!yieldedText && body.Length > 0)
            {
                _logger.Warning(
                    "VertexTeacher",
                    $"StreamChatWithToolsAsync: no text chunks parsed from stream body (len={body.Length}, head={TruncateForLog(body, 500)})");
            }
        }
        finally
        {
            reader?.Dispose();
            stream?.Dispose();
            responseStream?.Dispose();
        }
    }

    public async Task<Result<string>> GenerateTextAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct,
        object? responseJsonSchema = null)
    {
        if (!await IsConfiguredAsync(ct).ConfigureAwait(false))
            return Result<string>.Failure("Vertex credentials are not configured.");

        try
        {
            var token = await GetAccessTokenAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(token))
                return Result<string>.Failure("Could not obtain Google access token.");

            object? systemInstruction = null;
            if (!string.IsNullOrWhiteSpace(systemPrompt))
                systemInstruction = new { parts = new object[] { new { text = systemPrompt } } };

            object generationConfig;
            if (responseJsonSchema != null)
            {
                var (sTemp, sMax) = await GetStructuredGenerationParamsAsync(ct).ConfigureAwait(false);
                generationConfig = new
                {
                    temperature = sTemp,
                    maxOutputTokens = sMax,
                    responseMimeType = "application/json",
                    responseSchema = responseJsonSchema
                };
            }
            else
            {
                var (cTemp, cMax) = await GetChatGenerationParamsAsync(ct).ConfigureAwait(false);
                generationConfig = new { temperature = cTemp, maxOutputTokens = cMax };
            }

            var body = new
            {
                systemInstruction,
                contents = new object[]
                {
                    new { role = "user", parts = new object[] { new { text = userMessage } } }
                },
                generationConfig
            };

            var url = await BuildMethodUrlAsync("generateContent", ct).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning("VertexTeacher", $"generateContent failed: {(int)response.StatusCode} {responseText}");
                return Result<string>.Failure($"Vertex generate failed: HTTP {(int)response.StatusCode}");
            }

            var text = ExtractTextFromGenerateContentResponse(responseText);
            return string.IsNullOrWhiteSpace(text)
                ? Result<string>.Failure("Empty response from Gemini.")
                : Result<string>.Success(text.Trim());
        }
        catch (Exception ex)
        {
            _logger.Error("VertexTeacher", "GenerateTextAsync failed", ex);
            return Result<string>.Failure(ex.Message, ex);
        }
    }

    /// <summary>
    /// Maps a single <c>generateContent</c> JSON body to stream chunks. Preserves <c>parts</c> order (text vs tool).
    /// Supports <c>functionCall</c> / <c>function_call</c> and <c>args</c> / <c>arguments</c>.
    /// </summary>
    private static IEnumerable<StreamChunk> EnumerateChunksFromGenerateContentResponse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch
        {
            yield break;
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                yield break;

            var toolOrdinal = 0;
            foreach (var cand in candidates.EnumerateArray())
            {
                if (!cand.TryGetProperty("content", out var content))
                    continue;
                if (!content.TryGetProperty("parts", out var parts))
                    continue;

                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    {
                        var s = t.GetString();
                        if (!string.IsNullOrEmpty(s))
                            yield return new StreamChunk.Content(s);
                    }

                    if (!TryGetFunctionCallElement(part, out var fc))
                        continue;

                    var name = fc.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString() ?? ""
                        : "";
                    if (string.IsNullOrEmpty(name))
                        continue;

                    var argsJson = SerializeFunctionCallArgs(fc);
                    var id = "";
                    if (fc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                        id = idEl.GetString() ?? "";
                    var callId = string.IsNullOrEmpty(id) ? $"call_{toolOrdinal}" : id;
                    toolOrdinal++;

                    string? thoughtSig = null;
                    if (part.TryGetProperty("thoughtSignature", out var tsEl) && tsEl.ValueKind == JsonValueKind.String)
                        thoughtSig = tsEl.GetString();
                    else if (part.TryGetProperty("thought_signature", out var tsSnake) && tsSnake.ValueKind == JsonValueKind.String)
                        thoughtSig = tsSnake.GetString();

                    yield return new StreamChunk.ToolCall(new ToolCallRequest(callId, name, argsJson, thoughtSig));
                }
            }
        }
    }

    private static bool TryGetFunctionCallElement(JsonElement part, out JsonElement fc)
    {
        if (part.TryGetProperty("functionCall", out fc))
            return true;
        if (part.TryGetProperty("function_call", out fc))
            return true;
        fc = default;
        return false;
    }

    private static string SerializeFunctionCallArgs(JsonElement fc)
    {
        if (fc.TryGetProperty("args", out var argsEl))
            return SerializeArgsValue(argsEl);
        if (fc.TryGetProperty("arguments", out var argsEl2))
            return SerializeArgsValue(argsEl2);
        return "{}";
    }

    private static string SerializeArgsValue(JsonElement argsEl)
    {
        return argsEl.ValueKind switch
        {
            JsonValueKind.Object => argsEl.GetRawText(),
            JsonValueKind.Array => argsEl.GetRawText(),
            JsonValueKind.String => argsEl.GetString() ?? "{}",
            JsonValueKind.Null or JsonValueKind.Undefined => "{}",
            _ => argsEl.GetRawText()
        };
    }

    private static IEnumerable<string> ExtractStreamTextFragments(string line)
    {
        var doc = TryParseLine(line);
        if (doc == null) yield break;

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                yield break;
            var cand = candidates[0];
            if (!cand.TryGetProperty("content", out var content)) yield break;
            if (!content.TryGetProperty("parts", out var parts)) yield break;

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                {
                    var s = t.GetString();
                    if (!string.IsNullOrEmpty(s))
                        yield return s;
                }
            }
        }
    }

    private static JsonDocument? TryParseLine(string line)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractTextFromGenerateContentResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                return "";
            var cand = candidates[0];
            if (!cand.TryGetProperty("content", out var content)) return "";
            if (!content.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0)
                return "";
            var part = parts[0];
            if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString() ?? "";
        }
        catch
        {
            // ignored
        }

        return "";
    }

    /// <summary>
    /// Vertex <c>streamGenerateContent</c> may return SSE (<c>data: {...}</c>), a JSON array of chunks,
    /// newline-delimited JSON, or multiple concatenated objects without delimiters. Line-only reads often miss tokens.
    /// </summary>
    private static IEnumerable<string> EnumerateVertexStreamPayloadStrings(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) yield break;

        body = body.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');

        var lines = body.Split(['\r', '\n'], StringSplitOptions.None);
        if (lines.Any(l => l.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var line in lines)
            {
                var t = line.TrimStart();
                if (t.Length == 0) continue;
                if (t.StartsWith("event:", StringComparison.OrdinalIgnoreCase)) continue;
                if (!t.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                var payload = t[5..].Trim();
                if (payload == "[DONE]" || string.IsNullOrWhiteSpace(payload)) continue;
                yield return payload;
            }

            yield break;
        }

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch
        {
            // Multiple concatenated objects or partial JSON — handled below.
        }

        if (doc != null)
        {
            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in root.EnumerateArray())
                        yield return el.GetRawText();
                    yield break;
                }

                if (root.ValueKind == JsonValueKind.Object)
                {
                    yield return root.GetRawText();
                    yield break;
                }
            }
        }

        foreach (var chunk in SplitConcatenatedJsonObjects(body))
            yield return chunk;

        yield break;
    }

    /// <summary>Uses <see cref="Utf8JsonReader"/> outside of iterator methods (cannot cross yield boundaries).</summary>
    private static List<string> SplitConcatenatedJsonObjects(string body)
    {
        var list = new List<string>();
        var utf8 = Encoding.UTF8.GetBytes(body);
        var jsonReader = new Utf8JsonReader(utf8, new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        while (jsonReader.Read())
        {
            if (jsonReader.TokenType != JsonTokenType.StartObject)
                continue;
            using var chunkDoc = JsonDocument.ParseValue(ref jsonReader);
            list.Add(chunkDoc.RootElement.GetRawText());
        }

        if (list.Count == 0 && body.TrimStart().StartsWith('{'))
            list.Add(body);

        return list;
    }

    private static string TruncateForLog(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= maxLen) return s;
        return s[..maxLen] + "…";
    }

    private async Task<(double Temperature, int MaxOutputTokens)> GetChatGenerationParamsAsync(CancellationToken ct)
    {
        var t = await ReadDoubleSettingAsync(
            TeacherModelSettings.ChatTemperatureKey,
            TeacherModelSettings.DefaultChatTemperatureString,
            0.0,
            2.0,
            ct).ConfigureAwait(false);
        var m = await ReadIntSettingAsync(
            TeacherModelSettings.ChatMaxOutputTokensKey,
            TeacherModelSettings.DefaultChatMaxOutputTokensString,
            1,
            65535,
            ct).ConfigureAwait(false);
        return (t, m);
    }

    private async Task<(double Temperature, int MaxOutputTokens)> GetRoutingGenerationParamsAsync(CancellationToken ct)
    {
        var t = await ReadDoubleSettingAsync(
            TeacherModelSettings.RoutingTemperatureKey,
            TeacherModelSettings.DefaultRoutingTemperatureString,
            0.0,
            2.0,
            ct).ConfigureAwait(false);
        var m = await ReadIntSettingAsync(
            TeacherModelSettings.RoutingMaxOutputTokensKey,
            TeacherModelSettings.DefaultRoutingMaxOutputTokensString,
            1,
            8192,
            ct).ConfigureAwait(false);
        return (t, m);
    }

    private async Task<(double Temperature, int MaxOutputTokens)> GetStructuredGenerationParamsAsync(CancellationToken ct)
    {
        var t = await ReadDoubleSettingAsync(
            TeacherModelSettings.StructuredTemperatureKey,
            TeacherModelSettings.DefaultStructuredTemperatureString,
            0.0,
            2.0,
            ct).ConfigureAwait(false);
        var m = await ReadIntSettingAsync(
            TeacherModelSettings.StructuredMaxOutputTokensKey,
            TeacherModelSettings.DefaultStructuredMaxOutputTokensString,
            1,
            65535,
            ct).ConfigureAwait(false);
        return (t, m);
    }

    private async Task<double> ReadDoubleSettingAsync(string key, string defaultString, double min, double max, CancellationToken ct)
    {
        var raw = await _settings.GetAsync(key, defaultString).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
            raw = defaultString;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return Math.Clamp(v, min, max);
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
            return Math.Clamp(v, min, max);
        if (double.TryParse(defaultString, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return Math.Clamp(d, min, max);
        return min;
    }

    private async Task<int> ReadIntSettingAsync(string key, string defaultString, int min, int max, CancellationToken ct)
    {
        var raw = await _settings.GetAsync(key, defaultString).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
            raw = defaultString;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return Math.Clamp(v, min, max);
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.CurrentCulture, out v))
            return Math.Clamp(v, min, max);
        if (int.TryParse(defaultString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return Math.Clamp(i, min, max);
        return min;
    }

    private static JsonElement BuildRoutingResponseSchema()
    {
        const string json =
            """{"type":"object","properties":{"complexity":{"type":"string","enum":["simple","reasoning"]},"skills":{"type":"array","items":{"type":"string"},"minItems":1},"confidence":{"type":"string"},"reason":{"type":"string"}},"required":["complexity","skills"]}""";
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private async Task<string?> ResolveCredentialsPathAsync(CancellationToken ct)
    {
        var configured = await _settings.GetAsync(TeacherModelSettings.VertexCredentialsPathKey, "").ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var env = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return env;

        return null;
    }

    private async Task<GoogleCredential?> EnsureCredentialAsync(CancellationToken ct)
    {
        var path = await ResolveCredentialsPathAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(path))
            return null;

        if (_credential != null && string.Equals(_credentialPath, path, StringComparison.Ordinal))
            return _credential;

        await Task.Run(() =>
        {
            _credential = GoogleCredential.FromFile(path).CreateScoped("https://www.googleapis.com/auth/cloud-platform");
            _credentialPath = path;
        }, ct).ConfigureAwait(false);

        return _credential;
    }

    private async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        var cred = await EnsureCredentialAsync(ct).ConfigureAwait(false);
        if (cred == null) return null;
        return await cred.UnderlyingCredential.GetAccessTokenForRequestAsync(null, ct).ConfigureAwait(false);
    }

    private async Task<string> GetProjectIdAsync(CancellationToken ct)
    {
        var p = await _settings.GetAsync(TeacherModelSettings.VertexProjectIdKey, "").ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(p)) return p.Trim();
        return TeacherModelSettings.DefaultProjectId;
    }

    private async Task<string> GetLocationAsync(CancellationToken ct)
    {
        var l = await _settings.GetAsync(TeacherModelSettings.VertexLocationKey, "").ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(l)) return l.Trim();
        return TeacherModelSettings.DefaultLocation;
    }

    private async Task<string> GetModelIdAsync(CancellationToken ct)
    {
        var m = await _settings.GetAsync(TeacherModelSettings.VertexModelIdKey, "").ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(m)) return m.Trim();
        return TeacherModelSettings.DefaultModelId;
    }

    private async Task<string> BuildMethodUrlAsync(string method, CancellationToken ct = default)
    {
        var project = await GetProjectIdAsync(ct).ConfigureAwait(false);
        var location = await GetLocationAsync(ct).ConfigureAwait(false);
        var model = await GetModelIdAsync(ct).ConfigureAwait(false);
        var path = $"v1/projects/{project}/locations/{location}/publishers/google/models/{model}:{method}";
        // Global models use the non-regional host; regional uses {region}-aiplatform.googleapis.com.
        if (string.Equals(location, "global", StringComparison.OrdinalIgnoreCase))
            return $"https://aiplatform.googleapis.com/{path}";
        return $"https://{location}-aiplatform.googleapis.com/{path}";
    }

    public void Dispose() => _http.Dispose();
}
