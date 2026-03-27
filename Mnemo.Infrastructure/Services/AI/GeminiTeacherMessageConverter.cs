using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Maps OpenAI-style chat messages (as serialized from anonymous objects) to Vertex Gemini <c>contents</c> and <c>systemInstruction</c>.
/// </summary>
internal static class GeminiTeacherMessageConverter
{
    public static (object? SystemInstruction, List<object> Contents) Convert(IReadOnlyList<object> messages)
    {
        var json = JsonSerializer.Serialize(messages);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Messages must be a JSON array.");

        var systemText = new List<string>();
        var contents = new List<object>();

        foreach (var msg in doc.RootElement.EnumerateArray())
        {
            if (!msg.TryGetProperty("role", out var roleEl) || roleEl.ValueKind != JsonValueKind.String)
                continue;

            var role = roleEl.GetString() ?? "";

            if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase))
            {
                systemText.Add(ExtractTextContent(msg));
                continue;
            }

            if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
            {
                contents.Add(new { role = "user", parts = BuildUserParts(msg) });
                continue;
            }

            if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                if (msg.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array && tc.GetArrayLength() > 0)
                {
                    var parts = new List<object>();
                    var preambleText = ExtractTextContent(msg);
                    if (!string.IsNullOrEmpty(preambleText))
                        parts.Add(new { text = preambleText });

                    foreach (var call in tc.EnumerateArray())
                    {
                        if (!call.TryGetProperty("function", out var fn)) continue;
                        var name = fn.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                            ? n.GetString() ?? ""
                            : "";
                        object argsObj = new Dictionary<string, object>();
                        if (fn.TryGetProperty("arguments", out var argsEl))
                        {
                            if (argsEl.ValueKind == JsonValueKind.String)
                            {
                                try
                                {
                                    using var argsDoc = JsonDocument.Parse(argsEl.GetString() ?? "{}");
                                    argsObj = JsonSerializer.Deserialize<Dictionary<string, object>>(
                                        argsDoc.RootElement.GetRawText()) ?? argsObj;
                                }
                                catch
                                {
                                    argsObj = new Dictionary<string, object> { ["raw"] = argsEl.GetString() ?? "" };
                                }
                            }
                            else if (argsEl.ValueKind == JsonValueKind.Object)
                                argsObj = JsonSerializer.Deserialize<Dictionary<string, object>>(argsEl.GetRawText()) ?? argsObj;
                        }

                        parts.Add(new { functionCall = new { name, args = argsObj } });
                    }

                    if (parts.Count > 0)
                    {
                        contents.Add(new { role = "model", parts });
                        continue;
                    }
                }

                var text = ExtractTextContent(msg);
                if (!string.IsNullOrEmpty(text))
                    contents.Add(new { role = "model", parts = new object[] { new { text } } });
                continue;
            }

            if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                var name = msg.TryGetProperty("name", out var ne) && ne.ValueKind == JsonValueKind.String
                    ? ne.GetString() ?? "tool"
                    : "tool";
                var responseBody = msg.TryGetProperty("content", out var ce) ? ParseToolResponseContent(ce) : new Dictionary<string, object>();
                contents.Add(new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { functionResponse = new { name, response = responseBody } }
                    }
                });
            }
        }

        object? systemInstruction = null;
        if (systemText.Count > 0)
            systemInstruction = new { parts = new object[] { new { text = string.Join("\n", systemText) } } };

        return (systemInstruction, contents);
    }

    private static Dictionary<string, object> ParseToolResponseContent(JsonElement ce)
    {
        if (ce.ValueKind == JsonValueKind.String)
        {
            var s = ce.GetString() ?? "";
            try
            {
                using var d = JsonDocument.Parse(s);
                if (d.RootElement.ValueKind == JsonValueKind.Object)
                    return JsonSerializer.Deserialize<Dictionary<string, object>>(d.RootElement.GetRawText()) ?? new Dictionary<string, object>();
                return new Dictionary<string, object> { ["result"] = s };
            }
            catch
            {
                return new Dictionary<string, object> { ["result"] = s };
            }
        }

        if (ce.ValueKind == JsonValueKind.Object)
            return JsonSerializer.Deserialize<Dictionary<string, object>>(ce.GetRawText()) ?? new Dictionary<string, object>();

        return new Dictionary<string, object> { ["result"] = ce.ToString() };
    }

    private static string ExtractTextContent(JsonElement msg)
    {
        if (!msg.TryGetProperty("content", out var c))
            return "";

        if (c.ValueKind == JsonValueKind.String)
            return c.GetString() ?? "";

        if (c.ValueKind == JsonValueKind.Array)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var part in c.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var t) && t.GetString() == "text"
                    && part.TryGetProperty("text", out var tx))
                    sb.Append(tx.GetString());
            }

            return sb.ToString();
        }

        return "";
    }

    private static object[] BuildUserParts(JsonElement msg)
    {
        if (!msg.TryGetProperty("content", out var c))
            return Array.Empty<object>();

        if (c.ValueKind == JsonValueKind.String)
            return new object[] { new { text = c.GetString() ?? "" } };

        if (c.ValueKind != JsonValueKind.Array)
            return new object[] { new { text = c.GetRawText() } };

        var parts = new List<object>();
        foreach (var part in c.EnumerateArray())
        {
            if (part.TryGetProperty("type", out var typ) && typ.GetString() == "text"
                && part.TryGetProperty("text", out var tx))
            {
                parts.Add(new { text = tx.GetString() ?? "" });
            }
            else if (part.TryGetProperty("type", out var t2) && t2.GetString() == "image_url"
                     && part.TryGetProperty("image_url", out var iu)
                     && iu.TryGetProperty("url", out var urlEl))
            {
                var url = urlEl.GetString() ?? "";
                var (mime, b64) = ParseDataUrl(url);
                parts.Add(new { inlineData = new { mimeType = mime, data = b64 } });
            }
        }

        if (parts.Count == 0)
            return new object[] { new { text = "" } };

        return parts.ToArray();
    }

    private static (string Mime, string Data) ParseDataUrl(string url)
    {
        const string prefix = "data:";
        if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return ("image/jpeg", url);

        var semi = url.IndexOf(';');
        var comma = url.IndexOf(',');
        if (semi < 0 || comma < semi)
            return ("image/jpeg", url);

        var mime = url[prefix.Length..semi];
        if (mime.Contains("png", StringComparison.OrdinalIgnoreCase))
            mime = "image/png";
        else if (mime.Contains("webp", StringComparison.OrdinalIgnoreCase))
            mime = "image/webp";
        else
            mime = "image/jpeg";

        var data = url[(comma + 1)..];
        return (mime, data);
    }
}
