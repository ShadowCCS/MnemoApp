# MnemoApp AI Service

This module provides a comprehensive AI model management and inference system for MnemoApp.

## Architecture

The AI service is built with modularity in mind, allowing easy integration of different AI models and inference engines.

### Core Components

- **Models**: Data structures representing AI models, capabilities, and responses
- **Services**: Core business logic for model management and inference
- **API Integration**: Seamless integration with MnemoAPI for easy access

## Directory Structure

### Local Application Data

Models are stored in: `%LOCALAPPDATA%\MnemoApp\models\{model-name}\`

Each model directory contains:
- `{model}.gguf` - The actual model file
- `manifest.json` - Model metadata
- `capabilities.json` - Model capabilities and configuration
- `tokenizer.model` - Tokenizer for token counting (optional)

### LoRA Adapters

Adapters are stored in: `%LOCALAPPDATA%\MnemoApp\adapters\`

Structure:
- `adapters\{model-name}\{adapter}.bin` - Model-specific adapters
- `adapters\{adapter}.safetensors` - Generic adapters

## File Formats

### manifest.json
```json
{
  "name": "Display name for the UI",
  "originName": "Original model identifier",
  "version": "1.0.0",
  "author": "Model author",
  "description": "Model description",
  "modelType": "causal-lm",
  "license": "Apache 2.0"
}
```

### capabilities.json
This file defines HOW your system executes the model. It's the "driver" configuration.

#### For llama.cpp models:
```json
{
  "executionType": "llamacpp",
  "customDriver": "LlamaCppDriver",
  "supportsWebSearch": false,
  "supportsThinking": true,
  "maxContextLength": 8192,
  "promptTemplate": "<|im_start|>system\n{system_prompt}<|im_end|>\n<|im_start|>user\n{user_prompt}<|im_end|>\n<|im_start|>assistant\n",
  "stopTokens": ["<|im_end|>"],
  "systemPromptSupport": true,
  "multiTurnSupport": true,
  "thinkingPrompt": "Let me think step by step:",
  "executionConfig": {
    "executable": "llama-server.exe",
    "args": ["--model", "{model_path}", "--port", "8080", "--ctx-size", "{ctx}"],
    "port": 8080
  }
}
```

#### For Ollama:
```json
{
  "executionType": "openai",
  "httpEndpoint": "http://localhost:11434/v1/chat/completions",
  "openaiCompatible": true,
  "executionConfig": {
    "modelName": "gemma2:2b"
  }
}
```

#### For OpenAI API:
```json
{
  "executionType": "openai", 
  "httpEndpoint": "https://api.openai.com/v1/chat/completions",
  "apiKey": "your-key-here",
  "openaiCompatible": true,
  "executionConfig": {
    "modelName": "gpt-4o-mini"
  }
}
```

## Usage

### Accessing through MnemoAPI

```csharp
// Get all available models
var modelNames = await mnemoAPI.ai.GetAllNamesAsync();

// Get model details
var model = await mnemoAPI.ai.GetModelAsync("gemma-2b-it");

// Simple inference
var request = mnemoAPI.ai.CreateRequest("gemma-2b-it", "Hello, how are you?");
var response = await mnemoAPI.ai.InferAsync(request);

// Advanced inference with LoRA adapter
var advancedRequest = mnemoAPI.ai.CreateAdvancedRequest(
    modelName: "gemma-2b-it",
    prompt: "Explain quantum computing",
    systemPrompt: "You are a helpful AI assistant",
    loraAdapter: "science-adapter",
    temperature: 0.8f
);

// Token counting
var tokenCount = await mnemoAPI.ai.CountTokensAsync("Some text", "gemma-2b-it");

// Get available adapters
var adapters = await mnemoAPI.ai.GetAdaptersAsync();
```

### Model Discovery

The service automatically scans the models directory on initialization and provides events for updates:

```csharp
mnemoAPI.ai.SubscribeToModelUpdates(models => {
    // Handle model updates
    foreach (var model in models) {
        Console.WriteLine($"Model: {model.Manifest.Name}");
    }
});
```

## How It Works - Driver System

The key insight you had is correct! Each model needs specific execution code. Here's how it works:

### 1. Model Discovery
- System scans `%LOCALAPPDATA%\MnemoApp\models\` 
- Reads `manifest.json` and `capabilities.json` for each model
- Registers models with their execution requirements

### 2. Driver Selection
- `capabilities.json` specifies `executionType` and `customDriver`
- System picks the right driver: `LlamaCppDriver`, `OpenAICompatibleDriver`, etc.
- Each driver knows how to execute its type of model

### 3. Model Execution
- Driver handles starting processes, HTTP calls, prompt formatting
- `LlamaCppDriver`: Starts llama-server.exe, makes HTTP requests
- `OpenAICompatibleDriver`: Makes API calls to Ollama/OpenAI/etc.
- Each driver formats prompts according to model's template

### 4. Thinking & Features
- `supportsThinking`: Adds thinking prompt before user prompt
- `promptTemplate`: Defines how to format system/user messages
- `stopTokens`: Tells model when to stop generating

## Creating Custom Drivers

Want to support a new model type? Create a driver:

```csharp
public class MyCustomDriver : IModelDriver
{
    public string DriverName => "MyCustomDriver";
    
    public bool CanHandle(AIModel model) 
    {
        return model.Capabilities?.CustomDriver == "MyCustomDriver";
    }
    
    public async Task<AIInferenceResponse> InferAsync(AIModel model, AIInferenceRequest request, CancellationToken cancellationToken)
    {
        // Your custom execution logic here
        // - Start your inference engine
        // - Format the prompt
        // - Call your model
        // - Return response
    }
}
```

Register it: `_driverRegistry.RegisterDriver(new MyCustomDriver());`

## Extension Points

### Custom Tokenizers

Tokenization can be enhanced by:

1. Implementing actual tokenizer loading in `TokenizerService`
2. Supporting different tokenizer formats (SentencePiece, HuggingFace, etc.)
3. Adding tokenizer metadata to model capabilities

## Future Enhancements

- Integration with popular inference engines (llama.cpp, ONNX Runtime, etc.)
- Support for remote model serving
- Model quantization and optimization
- Batch inference support
- Model caching and memory management
