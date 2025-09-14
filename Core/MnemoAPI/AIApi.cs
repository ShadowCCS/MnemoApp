using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MnemoApp.Core.AI.Models;
using MnemoApp.Core.AI.Services;

namespace MnemoApp.Core.MnemoAPI
{
    /// <summary>
    /// AI-related API endpoints for MnemoAPI
    /// </summary>
    public class AIApi
    {
        private readonly IAIService _aiService;
        private readonly IModelSelectionService _modelSelectionService;

        public AIApi(IAIService aiService, IModelSelectionService modelSelectionService)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
            _modelSelectionService = modelSelectionService ?? throw new ArgumentNullException(nameof(modelSelectionService));
        }

        /// <summary>
        /// Get all available AI model names
        /// </summary>
        public async Task<IReadOnlyList<string>> GetAllNamesAsync()
        {
            return await _aiService.GetAllNamesAsync();
        }

        /// <summary>
        /// Get detailed information about all models
        /// </summary>
        public async Task<IReadOnlyList<AIModel>> GetAllModelsAsync()
        {
            return await _aiService.GetAllModelsAsync();
        }

        /// <summary>
        /// Get specific model information by name
        /// </summary>
        public async Task<AIModel?> GetModelAsync(string modelName)
        {
            return await _aiService.GetModelAsync(modelName);
        }

        /// <summary>
        /// Check if a model is available
        /// </summary>
        public async Task<bool> IsAvailableAsync(string modelName)
        {
            return await _aiService.IsModelAvailableAsync(modelName);
        }

        /// <summary>
        /// Get all available LoRA adapters
        /// </summary>
        public async Task<IReadOnlyList<LoraAdapter>> GetAdaptersAsync()
        {
            return await _aiService.GetAvailableAdaptersAsync();
        }

        /// <summary>
        /// Get LoRA adapters compatible with a specific model
        /// </summary>
        public async Task<IReadOnlyList<LoraAdapter>> GetCompatibleAdaptersAsync(string modelName)
        {
            return await _aiService.GetCompatibleAdaptersAsync(modelName);
        }

        /// <summary>
        /// Perform AI inference
        /// </summary>
        public async Task<AIInferenceResponse> InferAsync(AIInferenceRequest request, CancellationToken cancellationToken = default)
        {
            return await _aiService.InferAsync(request, cancellationToken);
        }

        /// <summary>
        /// Perform streaming AI inference
        /// </summary>
        public IAsyncEnumerable<string> InferStreamAsync(AIInferenceRequest request, CancellationToken cancellationToken = default)
        {
            return _aiService.InferStreamAsync(request, cancellationToken);
        }

        /// <summary>
        /// Count tokens in text using model's tokenizer
        /// </summary>
        public async Task<TokenCountResult> CountTokensAsync(string text, string modelName)
        {
            return await _aiService.CountTokensAsync(text, modelName);
        }

        /// <summary>
        /// Check if a tokenizer is available for a model
        /// </summary>
        public async Task<bool> HasTokenizerAsync(string modelName)
        {
            var model = await _aiService.GetModelAsync(modelName);
            return model?.HasTokenizer ?? false;
        }

        /// <summary>
        /// Refresh model registry (scan for new/removed models)
        /// </summary>
        public async Task RefreshAsync()
        {
            await _aiService.RefreshModelsAsync();
        }

        /// <summary>
        /// Create a simple inference request with just model name and prompt
        /// </summary>
        public AIInferenceRequest CreateRequest(string modelName, string prompt, string? systemPrompt = null)
        {
            return new AIInferenceRequest
            {
                ModelName = modelName,
                Prompt = prompt,
                SystemPrompt = systemPrompt
            };
        }

        /// <summary>
        /// Create an advanced inference request with full configuration
        /// </summary>
        public AIInferenceRequest CreateAdvancedRequest(
            string modelName, 
            string prompt, 
            string? systemPrompt = null,
            List<string>? conversationHistory = null,
            string? loraAdapter = null,
            float temperature = 0.7f,
            float topP = 0.9f,
            int topK = 40,
            float repeatPenalty = 1.1f,
            int maxTokens = 512,
            List<string>? stopTokens = null,
            bool stream = false)
        {
            return new AIInferenceRequest
            {
                ModelName = modelName,
                Prompt = prompt,
                SystemPrompt = systemPrompt,
                ConversationHistory = conversationHistory,
                LoraAdapter = loraAdapter,
                Temperature = temperature,
                TopP = topP,
                TopK = topK,
                RepeatPenalty = repeatPenalty,
                MaxTokens = maxTokens,
                StopTokens = stopTokens,
                Stream = stream
            };
        }

        /// <summary>
        /// Subscribe to model updates
        /// </summary>
        public void SubscribeToModelUpdates(Action<IReadOnlyList<AIModel>> callback)
        {
            _aiService.ModelsUpdated += callback;
        }

        /// <summary>
        /// Unsubscribe from model updates
        /// </summary>
        public void UnsubscribeFromModelUpdates(Action<IReadOnlyList<AIModel>> callback)
        {
            _aiService.ModelsUpdated -= callback;
        }

        /// <summary>
        /// Subscribe to adapter updates
        /// </summary>
        public void SubscribeToAdapterUpdates(Action<IReadOnlyList<LoraAdapter>> callback)
        {
            _aiService.AdaptersUpdated += callback;
        }

        /// <summary>
        /// Unsubscribe from adapter updates
        /// </summary>
        public void UnsubscribeFromAdapterUpdates(Action<IReadOnlyList<LoraAdapter>> callback)
        {
            _aiService.AdaptersUpdated -= callback;
        }

        // Model Selection API
        /// <summary>
        /// Get the currently selected model name
        /// </summary>
        public string? GetSelectedModel()
        {
            return _modelSelectionService.SelectedModel;
        }

        /// <summary>
        /// Set the selected model
        /// </summary>
        public void SetSelectedModel(string? modelName)
        {
            _modelSelectionService.SelectedModel = modelName;
        }

        /// <summary>
        /// Get available models as observable collection for UI binding
        /// </summary>
        public System.Collections.ObjectModel.ObservableCollection<string> GetAvailableModelsObservable()
        {
            return _modelSelectionService.AvailableModels;
        }

        /// <summary>
        /// Check if any models are available
        /// </summary>
        public bool HasModels()
        {
            return _modelSelectionService.HasModels;
        }

        /// <summary>
        /// Subscribe to model selection changes
        /// </summary>
        public void SubscribeToSelectedModelChanges(Action<string?> callback)
        {
            _modelSelectionService.SelectedModelChanged += callback;
        }

        /// <summary>
        /// Unsubscribe from model selection changes
        /// </summary>
        public void UnsubscribeFromSelectedModelChanges(Action<string?> callback)
        {
            _modelSelectionService.SelectedModelChanged -= callback;
        }

        /// <summary>
        /// Create a request using the currently selected model
        /// </summary>
        public AIInferenceRequest? CreateRequestWithSelectedModel(string prompt, string? systemPrompt = null)
        {
            var selectedModel = _modelSelectionService.SelectedModel;
            if (string.IsNullOrWhiteSpace(selectedModel))
                return null;

            return CreateRequest(selectedModel, prompt, systemPrompt);
        }

        /// <summary>
        /// Create an advanced request using the currently selected model
        /// </summary>
        public AIInferenceRequest? CreateAdvancedRequestWithSelectedModel(
            string prompt, 
            string? systemPrompt = null,
            List<string>? conversationHistory = null,
            string? loraAdapter = null,
            float temperature = 0.7f,
            float topP = 0.9f,
            int topK = 40,
            float repeatPenalty = 1.1f,
            int maxTokens = 512,
            List<string>? stopTokens = null,
            bool stream = false)
        {
            var selectedModel = _modelSelectionService.SelectedModel;
            if (string.IsNullOrWhiteSpace(selectedModel))
                return null;

            return CreateAdvancedRequest(selectedModel, prompt, systemPrompt, conversationHistory, loraAdapter, 
                temperature, topP, topK, repeatPenalty, maxTokens, stopTokens, stream);
        }
    }
}
