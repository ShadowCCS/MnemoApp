using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MnemoApp.Core.AI.Models
{
    /// <summary>
    /// Represents an AI model manifest from manifest.json
    /// </summary>
    public class AIModelManifest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("originName")]
        public string OriginName { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("modelType")]
        public string? ModelType { get; set; }

        [JsonPropertyName("license")]
        public string? License { get; set; }
    }

    /// <summary>
    /// Extended model capabilities and interaction configuration
    /// </summary>
    public class AIModelCapabilities
    {
        [JsonPropertyName("executionType")]
        public string? ExecutionType { get; set; }

        [JsonPropertyName("supportsWebSearch")]
        public bool SupportsWebSearch { get; set; } = false;

        [JsonPropertyName("supportsThinking")]
        public bool SupportsThinking { get; set; } = false;

        [JsonPropertyName("maxContextLength")]
        public int MaxContextLength { get; set; } = 2048;

        [JsonPropertyName("httpEndpoint")]
        public string? HttpEndpoint { get; set; }

        [JsonPropertyName("apiKey")]
        public string? ApiKey { get; set; }

        [JsonPropertyName("temperature")]
        public float DefaultTemperature { get; set; } = 0.7f;

        [JsonPropertyName("topP")]
        public float DefaultTopP { get; set; } = 0.9f;

        [JsonPropertyName("topK")]
        public int DefaultTopK { get; set; } = 40;

        [JsonPropertyName("repeatPenalty")]
        public float DefaultRepeatPenalty { get; set; } = 1.1f;

        [JsonPropertyName("promptTemplate")]
        public string? PromptTemplate { get; set; }

        [JsonPropertyName("stopTokens")]
        public List<string> StopTokens { get; set; } = new();

        [JsonPropertyName("systemPromptSupport")]
        public bool SystemPromptSupport { get; set; } = true;

        [JsonPropertyName("multiTurnSupport")]
        public bool MultiTurnSupport { get; set; } = true;

        [JsonPropertyName("thinkingPrompt")]
        public string? ThinkingPrompt { get; set; }

        [JsonPropertyName("executionConfig")]
        public Dictionary<string, object>? ExecutionConfig { get; set; }

        [JsonPropertyName("openaiCompatible")]
        public bool OpenAiCompatible { get; set; } = false;

        [JsonPropertyName("customDriver")]
        public string? CustomDriver { get; set; }
    }

    /// <summary>
    /// Complete AI model information including files and capabilities
    /// </summary>
    public class AIModel
    {
        public required string DirectoryPath { get; set; }
        public required string ModelFileName { get; set; }
        public required AIModelManifest Manifest { get; set; }
        public AIModelCapabilities? Capabilities { get; set; }
        public bool HasTokenizer { get; set; }
        public List<string> AvailableLoraAdapters { get; set; } = new();
        public DateTime LastModified { get; set; }
        public long ModelFileSize { get; set; }
    }

    /// <summary>
    /// LoRA adapter information
    /// </summary>
    public class LoraAdapter
    {
        public required string Name { get; set; }
        public required string FilePath { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; }
        public string? Author { get; set; }
        public DateTime LastModified { get; set; }
        public long FileSize { get; set; }
    }

    /// <summary>
    /// AI inference request parameters
    /// </summary>
    public class AIInferenceRequest
    {
        public required string ModelName { get; set; }
        public required string Prompt { get; set; }
        public string? SystemPrompt { get; set; }
        public List<string>? ConversationHistory { get; set; }
        public string? LoraAdapter { get; set; }
        public float Temperature { get; set; } = 0.7f;
        public float TopP { get; set; } = 0.9f;
        public int TopK { get; set; } = 40;
        public float RepeatPenalty { get; set; } = 1.1f;
        public int MaxTokens { get; set; } = 512;
        public List<string>? StopTokens { get; set; }
        public bool Stream { get; set; } = false;
    }

    /// <summary>
    /// AI inference response
    /// </summary>
    public class AIInferenceResponse
    {
        public bool Success { get; set; }
        public string? Response { get; set; }
        public string? ErrorMessage { get; set; }
        public int TokensGenerated { get; set; }
        public int TokensProcessed { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    /// <summary>
    /// Token count result
    /// </summary>
    public class TokenCountResult
    {
        public int TokenCount { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        /// <summary>
        /// Indicates if this is an estimated count (not from actual tokenization)
        /// </summary>
        public bool IsEstimate { get; set; }
    }
}
