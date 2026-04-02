using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Reads the raw <c>conversations.jsonl</c> capture file and produces two fine-tuning dataset files:
/// <list type="bullet">
///   <item><c>manager_dataset.jsonl</c> — one prompt/completion pair per turn for training the manager (routing + skill detection) model.</item>
///   <item><c>main_model_dataset.jsonl</c> — one messages-array entry per turn for training the main chat model (tool use, text responses).</item>
/// </list>
/// Both output files are placed alongside the source file unless an explicit output directory is specified.
/// Turns where <c>outcome.cancelled</c> is true or <c>outcome.foundResponse</c> is false are skipped by default
/// (note: turns that only executed tools with no streamed assistant tokens still set <c>foundResponse</c> when logged).
/// For a **canonical main-model example** with user-facing text before each tool round (valid <c>content</c> + <c>tool_calls</c>), see <c>docs/datasets/README.md</c>.
/// </summary>
public sealed class DatasetExporter
{
    private readonly ILoggerService _logger;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public DatasetExporter(ILoggerService logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Export manager and main-model datasets from <paramref name="sourceJsonlPath"/>.
    /// </summary>
    /// <param name="sourceJsonlPath">Path to <c>conversations.jsonl</c>.</param>
    /// <param name="outputDirectory">Directory for output files. Defaults to the same directory as <paramref name="sourceJsonlPath"/>.</param>
    /// <param name="skipCancelled">When true (default), turns that were cancelled by the user are omitted.</param>
    /// <param name="skipNoResponse">When true (default), turns where no response was produced are omitted.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Export statistics.</returns>
    public async Task<DatasetExportResult> ExportAsync(
        string sourceJsonlPath,
        string? outputDirectory = null,
        bool skipCancelled = true,
        bool skipNoResponse = true,
        CancellationToken ct = default)
    {
        if (!File.Exists(sourceJsonlPath))
            return DatasetExportResult.Failure($"Source file not found: {sourceJsonlPath}");

        var outDir = outputDirectory ?? Path.GetDirectoryName(sourceJsonlPath) ?? ".";
        Directory.CreateDirectory(outDir);

        var managerPath = Path.Combine(outDir, "manager_dataset.jsonl");
        var mainModelPath = Path.Combine(outDir, "main_model_dataset.jsonl");

        var lines = await File.ReadAllLinesAsync(sourceJsonlPath, ct).ConfigureAwait(false);

        var managerRows = new List<string>();
        var mainModelRows = new List<string>();
        var skippedCount = 0;
        var errorCount = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            ChatDatasetTurnRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<ChatDatasetTurnRecord>(line, ReadOptions);
            }
            catch (Exception ex)
            {
                _logger.Warning("DatasetExporter", $"Failed to parse line: {ex.Message}");
                errorCount++;
                continue;
            }

            if (record == null) continue;

            if (skipCancelled && record.Outcome.Cancelled)
            {
                skippedCount++;
                continue;
            }

            if (skipNoResponse && !record.Outcome.FoundResponse)
            {
                // Older logs marked "no response" when the model only ran tools and streamed no text tokens.
                var legacyToolTurn = record.Chat?.ToolRounds is { Count: > 0 };
                if (!legacyToolTurn)
                {
                    skippedCount++;
                    continue;
                }
            }

            // Manager dataset row
            if (record.Manager != null && record.Manager.Success)
            {
                var managerRow = BuildManagerRow(record.Manager);
                if (managerRow != null)
                    managerRows.Add(managerRow);
            }

            // Main model dataset row
            var mainRow = BuildMainModelRow(record);
            if (mainRow != null)
                mainModelRows.Add(mainRow);
        }

        await File.WriteAllLinesAsync(managerPath, managerRows, Encoding.UTF8, ct).ConfigureAwait(false);
        await File.WriteAllLinesAsync(mainModelPath, mainModelRows, Encoding.UTF8, ct).ConfigureAwait(false);

        return new DatasetExportResult
        {
            IsSuccess = true,
            ManagerDatasetPath = managerPath,
            MainModelDatasetPath = mainModelPath,
            ManagerRowCount = managerRows.Count,
            MainModelRowCount = mainModelRows.Count,
            SkippedTurnCount = skippedCount,
            ParseErrorCount = errorCount,
            TotalInputLines = lines.Length
        };
    }

    // ──────────────────────────────────────────────
    // Manager dataset
    // ──────────────────────────────────────────────

    /// <summary>
    /// Produces one JSONL row for the manager fine-tuning dataset.
    /// Format matches the v1 manager dataset: {"prompt": "...", "completion": "..."}
    /// The prompt is the exact user task block (TaskType + available skills + user message).
    /// The completion is the raw JSON routing decision the teacher model produced.
    /// </summary>
    private string? BuildManagerRow(ChatDatasetManagerSection manager)
    {
        var userBlock = manager.RoutingModelInput?.UserTaskBlock ?? manager.UserBlock;
        if (string.IsNullOrWhiteSpace(userBlock))
            return null;

        var completionRaw = manager.ResponseRaw;
        if (string.IsNullOrWhiteSpace(completionRaw))
        {
            // Fall back to rebuilding completion from ParsedDecision
            if (manager.ParsedDecision == null)
                return null;

            completionRaw = BuildCompletionFromDecision(manager.ParsedDecision);
        }
        else
        {
            // Normalise: strip markdown fences if present, collapse to single line
            completionRaw = NormaliseJsonCompletion(completionRaw);
        }

        if (string.IsNullOrWhiteSpace(completionRaw))
            return null;

        var row = new { prompt = userBlock, completion = completionRaw };
        return JsonSerializer.Serialize(row, WriteOptions);
    }

    private static string BuildCompletionFromDecision(ChatDatasetRoutingDecision d)
    {
        var obj = new JsonObject();
        if (!string.IsNullOrWhiteSpace(d.Complexity))
            obj["complexity"] = d.Complexity.ToLowerInvariant();

        var arr = new JsonArray();
        if (d.Skills is { Count: > 0 })
        {
            foreach (var s in d.Skills)
                arr.Add(s);
        }
        else if (!string.IsNullOrWhiteSpace(d.LegacySkill))
            arr.Add(d.LegacySkill.Trim());
        else
            arr.Add("NONE");
        obj["skills"] = arr;

        if (!string.IsNullOrWhiteSpace(d.Confidence))
            obj["confidence"] = d.Confidence.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(d.Reason))
            obj["reason"] = d.Reason;
        return obj.ToJsonString();
    }

    private static string NormaliseJsonCompletion(string raw)
    {
        var s = raw.Trim();
        // Strip ```json ... ``` fences
        if (s.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = s.IndexOf('\n');
            if (firstNewline >= 0) s = s[(firstNewline + 1)..];
            if (s.EndsWith("```", StringComparison.Ordinal))
                s = s[..^3].TrimEnd();
        }
        // Collapse to one line (the training format is single-line JSON)
        return s.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Trim();
    }

    // ──────────────────────────────────────────────
    // Main model dataset
    // ──────────────────────────────────────────────

    /// <summary>
    /// Produces one JSONL row for the main model fine-tuning dataset.
    /// Uses OpenAI chat fine-tuning format: {"messages": [...]}
    ///
    /// For tool turns, the complete agentic loop is unrolled into the message list:
    ///   system → user → [assistant (tool_calls) → tool (result) → ...]* → assistant (final text)
    ///
    /// For plain turns, it is simply:
    ///   system → [prior history] → user → assistant
    /// </summary>
    private string? BuildMainModelRow(ChatDatasetTurnRecord record)
    {
        var chat = record.Chat;
        if (chat == null)
            return null;

        // Determine the final assistant response (may be empty after tool rounds — still valid for training)
        var finalResponse = record.FinalAssistantResponse
            ?? (string.IsNullOrWhiteSpace(chat.AssistantResponse) ? null : chat.AssistantResponse);

        var hasToolRounds = chat.ToolRounds is { Count: > 0 };
        if (string.IsNullOrWhiteSpace(finalResponse) && !hasToolRounds)
            return null;

        finalResponse ??= string.Empty;

        var messages = new List<object>();

        // System prompt
        var systemPrompt = chat.SystemPrompt;
        if (string.IsNullOrWhiteSpace(systemPrompt))
            systemPrompt = record.ComposedSystemPrompt;
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new { role = "system", content = systemPrompt });

        if (chat.ToolRounds != null && chat.ToolRounds.Count > 0)
        {
            // Tool path: build the full agentic conversation from scratch
            // Start with any prior history (excluding system and the final user message which we add ourselves)
            if (chat.MessageHistory != null)
            {
                foreach (var msg in chat.MessageHistory)
                {
                    if (msg.Role == "system") continue; // already added above
                    // Skip the trailing user message — we'll add it manually after history
                    // (MessageHistory includes the current user message at the end)
                    messages.Add(BuildRawMessage(msg));
                }
            }
            else
            {
                // No history: just the user message
                messages.Add(new { role = "user", content = record.LatestUserMessage });
            }

            // Unroll each tool round into the message list
            foreach (var round in chat.ToolRounds)
            {
                foreach (var call in round.ToolCalls)
                {
                    // Assistant asking for the tool call
                    messages.Add(new
                    {
                        role = "assistant",
                        content = (string?)null,
                        tool_calls = new[]
                        {
                            new
                            {
                                id = call.ToolCallId,
                                type = "function",
                                function = new
                                {
                                    name = call.Name,
                                    arguments = call.ArgumentsJson ?? "{}"
                                }
                            }
                        }
                    });

                    // Tool result
                    messages.Add(new
                    {
                        role = "tool",
                        tool_call_id = call.ToolCallId,
                        name = call.Name,
                        content = call.ResultContent ?? ""
                    });
                }
            }

            // Final assistant text response
            messages.Add(new { role = "assistant", content = finalResponse });
        }
        else
        {
            // Non-tool path: history already contains the system, prior turns, and current user message.
            // If MessageHistory is available use it; otherwise fall back to minimal system + user.
            if (chat.MessageHistory != null && chat.MessageHistory.Count > 0)
            {
                foreach (var msg in chat.MessageHistory)
                {
                    if (msg.Role == "system") continue; // already added
                    messages.Add(BuildRawMessage(msg));
                }
            }
            else
            {
                messages.Add(new { role = "user", content = record.LatestUserMessage });
            }

            messages.Add(new { role = "assistant", content = finalResponse });
        }

        // Build the tools array if present (for models that support tool declarations in fine-tune data)
        List<object>? toolDefs = null;
        if (chat.ActiveTools != null && chat.ActiveTools.Count > 0)
        {
            toolDefs = new List<object>(chat.ActiveTools.Count);
            foreach (var t in chat.ActiveTools)
            {
                JsonNode? paramsNode = null;
                if (!string.IsNullOrWhiteSpace(t.ParametersJson))
                {
                    try { paramsNode = JsonNode.Parse(t.ParametersJson); }
                    catch { /* ignore malformed */ }
                }

                toolDefs.Add(new
                {
                    type = "function",
                    function = new
                    {
                        name = t.Name,
                        description = t.Description,
                        parameters = paramsNode
                    }
                });
            }
        }

        object row = toolDefs != null
            ? (object)new { messages, tools = toolDefs }
            : new { messages };

        return JsonSerializer.Serialize(row, WriteOptions);
    }

    private static object BuildRawMessage(ChatDatasetMessage msg)
    {
        if (msg.Role == "tool")
        {
            return new
            {
                role = msg.Role,
                tool_call_id = msg.ToolCallId,
                name = msg.ToolName,
                content = msg.Content ?? ""
            };
        }

        return new { role = msg.Role, content = msg.Content ?? "" };
    }

    // ──────────────────────────────────────────────
    // Memory system dataset exports
    // ──────────────────────────────────────────────

    /// <summary>
    /// Writes a <c>convo_summarize_dataset.jsonl</c> file from the provided generated examples.
    /// Each line is a <c>{ prompt, completion }</c> pair matching the manager fine-tuning format.
    /// </summary>
    /// <param name="examples">Examples produced by <see cref="ConvoSummarizeDatasetBuilder"/>.</param>
    /// <param name="outputPath">Full path for the output JSONL file.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<DatasetMemoryExportResult> ExportManagerSummarizationDatasetAsync(
        IReadOnlyList<DatasetSummarizationExample> examples,
        string outputPath,
        CancellationToken ct = default)
    {
        if (examples == null || examples.Count == 0)
            return new DatasetMemoryExportResult { IsSuccess = true, RowCount = 0, OutputPath = outputPath };

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        var lines = examples
            .Where(e => !string.IsNullOrWhiteSpace(e.Prompt) && !string.IsNullOrWhiteSpace(e.Completion))
            .Select(e => JsonSerializer.Serialize(new { prompt = e.Prompt, completion = e.Completion }, WriteOptions))
            .ToList();

        await File.WriteAllLinesAsync(outputPath, lines, Encoding.UTF8, ct).ConfigureAwait(false);

        var bySeedType = examples
            .GroupBy(e => e.SeedType)
            .ToDictionary(g => g.Key, g => g.Count());

        _logger.Info("DatasetExporter",
            $"Wrote {lines.Count} convo_summarize examples to {outputPath} " +
            $"({string.Join(", ", bySeedType.Select(kvp => $"{kvp.Key}:{kvp.Value}"))})");

        return new DatasetMemoryExportResult
        {
            IsSuccess = true,
            RowCount = lines.Count,
            OutputPath = outputPath,
            RowsBySeedType = bySeedType
        };
    }

    /// <summary>
    /// Writes a <c>routing_with_context_dataset.jsonl</c> file from the provided generated examples.
    /// Each line is a <c>{ prompt, completion }</c> pair for context-aware routing fine-tuning.
    /// </summary>
    /// <param name="examples">Examples produced by <see cref="RoutingWithContextDatasetBuilder"/>.</param>
    /// <param name="outputPath">Full path for the output JSONL file.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<DatasetMemoryExportResult> ExportRoutingWithContextDatasetAsync(
        IReadOnlyList<DatasetRoutingWithContextExample> examples,
        string outputPath,
        CancellationToken ct = default)
    {
        if (examples == null || examples.Count == 0)
            return new DatasetMemoryExportResult { IsSuccess = true, RowCount = 0, OutputPath = outputPath };

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        var lines = examples
            .Where(e => !string.IsNullOrWhiteSpace(e.Prompt) && !string.IsNullOrWhiteSpace(e.Completion))
            .Select(e => JsonSerializer.Serialize(new { prompt = e.Prompt, completion = e.Completion }, WriteOptions))
            .ToList();

        await File.WriteAllLinesAsync(outputPath, lines, Encoding.UTF8, ct).ConfigureAwait(false);

        var bySeedType = examples
            .GroupBy(e => e.SeedType)
            .ToDictionary(g => g.Key, g => g.Count());

        var shortFollowUpCount = examples.Count(e => e.IsShortFollowUp);
        _logger.Info("DatasetExporter",
            $"Wrote {lines.Count} routing_with_context examples to {outputPath} " +
            $"(short_followup={shortFollowUpCount}, " +
            $"{string.Join(", ", bySeedType.Select(kvp => $"{kvp.Key}:{kvp.Value}"))})");

        return new DatasetMemoryExportResult
        {
            IsSuccess = true,
            RowCount = lines.Count,
            OutputPath = outputPath,
            RowsBySeedType = bySeedType,
            ShortFollowUpCount = shortFollowUpCount
        };
    }
}

/// <summary>Summary of a completed export operation.</summary>
public sealed class DatasetExportResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ManagerDatasetPath { get; init; }
    public string? MainModelDatasetPath { get; init; }
    public int ManagerRowCount { get; init; }
    public int MainModelRowCount { get; init; }
    public int SkippedTurnCount { get; init; }
    public int ParseErrorCount { get; init; }
    public int TotalInputLines { get; init; }

    public static DatasetExportResult Failure(string message) =>
        new() { IsSuccess = false, ErrorMessage = message };
}

/// <summary>Summary of a memory-system dataset export (summarization or routing-with-context).</summary>
public sealed class DatasetMemoryExportResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public string? OutputPath { get; init; }
    public int RowCount { get; init; }
    public Dictionary<string, int> RowsBySeedType { get; init; } = new();
    public int ShortFollowUpCount { get; init; }
}
