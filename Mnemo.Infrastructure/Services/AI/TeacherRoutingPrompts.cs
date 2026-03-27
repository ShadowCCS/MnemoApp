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
        + "\"skill\" = one of the listed skills or NONE; "
        + "optional \"confidence\" (high|medium|low); optional \"reason\" (short). "
        + "simple = quick answers; reasoning = multi-step, deep analysis, or long writing. "
        + "Choose Application for Mnemo-the-product questions: settings, navigation, themes, what the app does, app-wide help. "
        + "Choose Notes, Mindmap, or Path only when the user is clearly about that module. "
        + "Choose NONE for general study/subject chat with no app or module tie. "
        + "If [RECENT TOOL CONTEXT] appears, short follow-ups that continue that action should keep that context's skill.";
}
