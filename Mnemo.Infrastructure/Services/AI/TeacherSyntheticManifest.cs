using System.Collections.Generic;
using Mnemo.Core.Models;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Placeholder manifest for Vertex Gemini when developer "teacher main" mode is on (not loaded from disk registry).
/// </summary>
public static class TeacherSyntheticManifest
{
    public const string ChatModelId = "teacher:vertex/gemini-3.1-flash-lite-preview";

    public static bool IsTeacher(AIModelManifest? manifest) =>
        manifest != null && string.Equals(manifest.Id, ChatModelId, StringComparison.Ordinal);

    public static AIModelManifest CreateChatModel() => new()
    {
        Id = ChatModelId,
        DisplayName = "Gemini 3.1 Flash Lite Preview (teacher)",
        Type = AIModelType.Text,
        Role = AIModelRoles.Low,
        PromptTemplate = "LLAMA3",
        Endpoint = null,
        LocalPath = string.Empty,
        Metadata = new Dictionary<string, string>
        {
            ["Temperature"] = "0.7",
            ["MaxTokens"] = "8192"
        }
    };
}
