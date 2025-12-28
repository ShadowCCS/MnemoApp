using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface IAIOrchestrator
{
    Task<Result<string>> PromptAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
    IAsyncEnumerable<string> PromptStreamingAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
    Task<Result<string>> PromptWithModelAsync(string modelId, string prompt, CancellationToken ct = default);
    
    // RAG enabled prompt
    Task<Result<string>> PromptWithContextAsync(string prompt, IEnumerable<KnowledgeChunk> context, CancellationToken ct = default);
}


