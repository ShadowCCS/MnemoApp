namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// System instructions for Gemini when substituting the fine-tuned local manager (routing JSON contract).
/// </summary>
internal static class TeacherRoutingPrompts
{
    public const string SystemInstruction =
        "You route user messages for the Mnemo desktop app. "
        + "Return one JSON object only (no markdown, no extra text): "
        + "\"complexity\" = \"simple\" or \"reasoning\"; "
        + "\"skills\" = JSON array of one or more distinct skill ids in execution order. "
        + "Single module: [\"Notes\"]. General study chat with no app tools: [\"NONE\"]. "
        + "Multi-module in one turn (e.g. read a note then create a mindmap): [\"Notes\",\"Mindmap\"]. "
        + "optional \"confidence\" (high|medium|low); optional \"reason\" (short). "
        + "simple = quick answers; reasoning = multi-step, deep analysis, or long writing. "
        + "Choose Application for Mnemo-the-product questions: navigation, themes, what the app does, app-wide help. "
        + "Choose Settings when the user wants to read or change allowlisted app preferences (language, editor/AI toggles, display name). "
        + "Choose Notes, Mindmap, or Path only when the user is clearly about that module. "
        + "Choose NONE for general study/subject chat with no app or module tie. "
        + "If [RECENT TOOL CONTEXT] appears, short follow-ups that continue that action should keep that context's skill. "
        + "Use multiple entries in \"skills\" only when the same user message clearly requires capabilities from more than one module.";
}
