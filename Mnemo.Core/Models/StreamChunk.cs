namespace Mnemo.Core.Models;

/// <summary>
/// Discriminated union representing a single unit from a streaming generation response.
/// Assistant-visible text, optional model reasoning (e.g. OpenAI-style <c>reasoning_content</c>), or a tool call.
/// </summary>
public abstract class StreamChunk
{
    private StreamChunk() { }

    /// <summary>A text token to display in the main assistant message.</summary>
    public sealed class Content : StreamChunk
    {
        public string Token { get; }
        public Content(string token) => Token = token;
    }

    /// <summary>
    /// A reasoning / thought token from a thinking-capable model (streamed separately from <see cref="Content"/>).
    /// </summary>
    public sealed class Reasoning : StreamChunk
    {
        public string Token { get; }
        public Reasoning(string token) => Token = token;
    }

    /// <summary>A fully-assembled tool call the model wants to execute.</summary>
    public sealed class ToolCall : StreamChunk
    {
        public ToolCallRequest Request { get; }
        public ToolCall(ToolCallRequest request) => Request = request;
    }
}
