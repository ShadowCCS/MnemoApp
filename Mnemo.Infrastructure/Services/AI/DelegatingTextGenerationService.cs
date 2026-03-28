using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.AI;

/// <summary>
/// Routes non-streaming generation to Vertex Gemini when the manifest is the synthetic teacher model; otherwise uses the local llama HTTP service.
/// Streaming is handled inside <see cref="AIOrchestrator"/> so system/user prompts stay consistent for the teacher path.
/// </summary>
public sealed class DelegatingTextGenerationService : ITextGenerationService
{
    private readonly LlamaCppHttpTextService _llama;
    private readonly ITeacherModelClient _teacher;
    private readonly ISettingsService _settings;

    public DelegatingTextGenerationService(
        LlamaCppHttpTextService llama,
        ITeacherModelClient teacher,
        ISettingsService settings)
    {
        _llama = llama;
        _teacher = teacher;
        _settings = settings;
    }

    public async Task<Result<string>> GenerateAsync(AIModelManifest manifest, string prompt, CancellationToken ct, object? responseJsonSchema = null)
    {
        if (!TeacherSyntheticManifest.IsTeacher(manifest))
            return await _llama.GenerateAsync(manifest, prompt, ct, responseJsonSchema).ConfigureAwait(false);

        if (!await _settings.GetAsync(TeacherModelSettings.UseTeacherMainChatKey, false).ConfigureAwait(false))
            return Result<string>.Failure("Teacher main chat is not enabled.");

        if (!await _teacher.IsConfiguredAsync(ct).ConfigureAwait(false))
            return Result<string>.Failure("Vertex teacher credentials are not configured.");

        return await _teacher.GenerateTextAsync(string.Empty, prompt, ct, responseJsonSchema).ConfigureAwait(false);
    }

    public IAsyncEnumerable<StreamChunk> GenerateStreamingAsync(
        AIModelManifest manifest,
        string prompt,
        CancellationToken ct,
        IReadOnlyList<string>? imageBase64Contents = null) =>
        _llama.GenerateStreamingAsync(manifest, prompt, ct, imageBase64Contents);

    public IAsyncEnumerable<StreamChunk> GenerateStreamingWithToolsAsync(
        AIModelManifest manifest,
        IReadOnlyList<object> messages,
        IReadOnlyList<SkillToolDefinition> tools,
        CancellationToken ct) =>
        _llama.GenerateStreamingWithToolsAsync(manifest, messages, tools, ct);

    public void UnloadModel(string modelId) => _llama.UnloadModel(modelId);

    public void Dispose() => _llama.Dispose();
}
