using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MnemoApp.Core.AI.Models;

namespace MnemoApp.Core.AI.Services
{
    /// <summary>
    /// Core AI service interface for model management and inference
    /// </summary>
    public interface IAIService
    {
        /// <summary>
        /// Initialize the AI service and discover available models
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Get all available model names
        /// </summary>
        Task<IReadOnlyList<string>> GetAllNamesAsync();

        /// <summary>
        /// Get detailed information about all models
        /// </summary>
        Task<IReadOnlyList<AIModel>> GetAllModelsAsync();

        /// <summary>
        /// Get specific model information by name
        /// </summary>
        Task<AIModel?> GetModelAsync(string modelName);

        /// <summary>
        /// Check if a model exists and is available
        /// </summary>
        Task<bool> IsModelAvailableAsync(string modelName);

        /// <summary>
        /// Get all available LoRA adapters
        /// </summary>
        Task<IReadOnlyList<LoraAdapter>> GetAvailableAdaptersAsync();

        /// <summary>
        /// Get LoRA adapters compatible with a specific model
        /// </summary>
        Task<IReadOnlyList<LoraAdapter>> GetCompatibleAdaptersAsync(string modelName);

        /// <summary>
        /// Perform AI inference
        /// </summary>
        Task<AIInferenceResponse> InferAsync(AIInferenceRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Perform streaming AI inference
        /// </summary>
        IAsyncEnumerable<string> InferStreamAsync(AIInferenceRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Count tokens in text using model's tokenizer
        /// </summary>
        Task<TokenCountResult> CountTokensAsync(string text, string modelName);

        /// <summary>
        /// Refresh model registry (scan for new/removed models)
        /// </summary>
        Task RefreshModelsAsync();

        /// <summary>
        /// Event fired when models are discovered or updated
        /// </summary>
        event Action<IReadOnlyList<AIModel>>? ModelsUpdated;

        /// <summary>
        /// Event fired when adapters are discovered or updated
        /// </summary>
        event Action<IReadOnlyList<LoraAdapter>>? AdaptersUpdated;
    }
}
