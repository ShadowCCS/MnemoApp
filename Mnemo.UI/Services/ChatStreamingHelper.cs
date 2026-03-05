using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Services;

namespace Mnemo.UI.Services;

/// <summary>
/// Shared streaming chat logic used by the Chat module and Right Sidebar assistant.
/// </summary>
public static class ChatStreamingHelper
{
    /// <summary>System prompt for General mode: helpful assistant.</summary>
    public const string GeneralSystemPrompt = @"You are a helpful AI assistant in the Mnemo application.

When answering:
- Use Markdown formatting
- Use tables for comparisons or structured data (regular markdown format is supported, if asked for a table do not make it in a code block)
- Use LaTeX for equations when appropriate
- Prefer clarity and structure over prose
- Your default language is english";

    /// <summary>System prompt for Explainer mode: focus on teaching and breaking down concepts.</summary>
    public const string ExplainerSystemPrompt = @"You are an explainer assistant in the Mnemo application. Your role is to teach and clarify.

When answering:
- Break complex ideas into simple steps
- Use examples and analogies where helpful
- Use Markdown formatting, tables for comparisons, and LaTeX for equations when appropriate
- Prefer clarity and structure; avoid unnecessary jargon unless the user is clearly familiar with the topic
- Your default language is english";

    /// <summary>Default system prompt (General). Kept for backward compatibility.</summary>
    public static string DefaultSystemPrompt => GeneralSystemPrompt;

    /// <summary>Returns the system prompt for the given assistant mode (e.g. ""General"", ""Explainer"").</summary>
    public static string GetSystemPromptForMode(string mode)
    {
        return string.Equals(mode, "Explainer", StringComparison.OrdinalIgnoreCase)
            ? ExplainerSystemPrompt
            : GeneralSystemPrompt;
    }

    /// <summary>Max UI updates per second when streaming; throttle interval in ms.</summary>
    public const int StreamingThrottleMs = 200;

    /// <summary>Max number of recent messages to include in context (conversation window).</summary>
    public const int MaxContextMessageCount = 11;

    /// <summary>
    /// Builds conversation context from recent messages for the prompt.
    /// </summary>
    /// <param name="messages">All messages (newest at end).</param>
    /// <param name="excludeMessage">Optional message to exclude (e.g. the placeholder AI message being filled).</param>
    /// <param name="isUser">Predicate: true if the message is from the user.</param>
    /// <param name="getContent">Selector for message content.</param>
    public static string BuildContextFromMessages<T>(
        IList<T> messages,
        T? excludeMessage,
        Func<T, bool> isUser,
        Func<T, string> getContent)
    {
        var recent = messages
            .TakeLast(MaxContextMessageCount)
            .Where(m => !ReferenceEquals(m, excludeMessage))
            .ToList();
        if (recent.Count <= 1) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("Previous conversation history:");
        foreach (var msg in recent)
            sb.AppendLine($"{(isUser(msg) ? "User" : "Assistant")}: {getContent(msg)}");
        return sb.ToString();
    }

    /// <summary>
    /// Runs the streaming prompt loop off the UI thread and reports content via callbacks.
    /// Callbacks may be invoked from a background thread; caller must marshal to UI for updates.
    /// </summary>
    /// <param name="imageBase64Contents">Optional. For vision: list of image base64 strings to send with the prompt.</param>
    /// <returns>True if at least one token was received; false if empty response.</returns>
    public static async Task<(bool FoundResponse, string FinalContent)> RunStreamingAsync(
        IAIOrchestrator orchestrator,
        string systemPrompt,
        string fullPrompt,
        CancellationToken cancellationToken,
        Action<string> onContentUpdate,
        IReadOnlyList<string>? imageBase64Contents = null)
    {
        var buffer = new StringBuilder();
        var lastUiUpdate = DateTime.UtcNow;
        var foundResponse = false;

        await Task.Run(async () =>
        {
            await foreach (var token in orchestrator.PromptStreamingAsync(systemPrompt, fullPrompt, cancellationToken, imageBase64Contents))
            {
                buffer.Append(token);
                foundResponse = true;

                var now = DateTime.UtcNow;
                var content = buffer.ToString();
                if ((now - lastUiUpdate).TotalMilliseconds >= StreamingThrottleMs || token.Contains('\n'))
                {
                    lastUiUpdate = now;
                    onContentUpdate(content);
                }
            }
        }, cancellationToken).ConfigureAwait(false);

        var finalContent = buffer.ToString();
        return (foundResponse, finalContent);
    }
}
