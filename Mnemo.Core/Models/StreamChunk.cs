namespace Mnemo.Core.Models;

/// <summary>
/// Discriminated union representing a single unit from a streaming generation response.
/// Either a text token or a completed tool-call request from the model.
/// </summary>
public abstract class StreamChunk
{
    private StreamChunk() { }

    /// <summary>A text token to display.</summary>
    public sealed class Content : StreamChunk
    {
        public string Token { get; }
        public Content(string token) => Token = token;
    }

    /// <summary>A fully-assembled tool call the model wants to execute.</summary>
    public sealed class ToolCall : StreamChunk
    {
        public ToolCallRequest Request { get; }
        public ToolCall(ToolCallRequest request) => Request = request;
    }
}
