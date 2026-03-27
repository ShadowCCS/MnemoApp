namespace Mnemo.Core.Services;

/// <summary>Developer-only switches and Vertex AI configuration for the Gemini teacher model (fine-tuning data collection).</summary>
public static class TeacherModelSettings
{
    /// <summary>When true, routing / skill classification uses the teacher model instead of the local manager model.</summary>
    public const string UseTeacherRoutingKey = "Developer.TeacherModelRouting";

    /// <summary>When true, main chat generation uses the teacher model instead of local tier models (when credentials are valid).</summary>
    public const string UseTeacherMainChatKey = "Developer.TeacherModelMainChat";

    /// <summary>Optional absolute path to a Google Cloud service account JSON key. If empty, <c>GOOGLE_APPLICATION_CREDENTIALS</c> is used.</summary>
    public const string VertexCredentialsPathKey = "Developer.VertexCredentialsPath";

    /// <summary>Google Cloud project id (defaults match Vertex setup in developer docs).</summary>
    public const string VertexProjectIdKey = "Developer.VertexProjectId";

    /// <summary>Vertex region, e.g. europe-north1.</summary>
    public const string VertexLocationKey = "Developer.VertexLocation";

    /// <summary>Model id under publishers/google/models, e.g. gemini-2.5-flash.</summary>
    public const string VertexModelIdKey = "Developer.VertexModelId";

    /// <summary>Chat / streaming / tools: temperature (0–2), stored as string for text field parsing.</summary>
    public const string ChatTemperatureKey = "Developer.TeacherChatTemperature";

    /// <summary>Chat / streaming / tools: max output tokens (Gemini caps vary by model; typical 1–65535).</summary>
    public const string ChatMaxOutputTokensKey = "Developer.TeacherChatMaxOutputTokens";

    /// <summary>Routing JSON: temperature (usually 0 for deterministic classification).</summary>
    public const string RoutingTemperatureKey = "Developer.TeacherRoutingTemperature";

    /// <summary>Routing JSON: max output tokens.</summary>
    public const string RoutingMaxOutputTokensKey = "Developer.TeacherRoutingMaxOutputTokens";

    /// <summary>Non-streaming generate with JSON schema (e.g. learning path): temperature.</summary>
    public const string StructuredTemperatureKey = "Developer.TeacherStructuredTemperature";

    /// <summary>Non-streaming generate with JSON schema: max output tokens.</summary>
    public const string StructuredMaxOutputTokensKey = "Developer.TeacherStructuredMaxOutputTokens";

    /// <summary>
    /// When the Vertex teacher is the main chat model, this text is appended to the composed system prompt (after skill injection)
    /// to steer answer format, tone, and structure for dataset collection or product voice.
    /// </summary>
    public const string ChatStylePromptKey = "Developer.TeacherChatStylePrompt";

    public const string DefaultProjectId = "mnmo-490214";
    public const string DefaultLocation = "europe-north1";
    public const string DefaultModelId = "gemini-2.5-flash";

    public const string DefaultChatTemperatureString = "0.7";
    public const string DefaultChatMaxOutputTokensString = "65536";
    public const string DefaultRoutingTemperatureString = "0";
    public const string DefaultRoutingMaxOutputTokensString = "512";
    public const string DefaultStructuredTemperatureString = "0.2";
    public const string DefaultStructuredMaxOutputTokensString = "8192";
}
