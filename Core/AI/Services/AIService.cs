using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MnemoApp.Core.AI.Models;
using MnemoApp.Core.AI.Drivers;

namespace MnemoApp.Core.AI.Services
{
    /// <summary>
    /// Implementation of AI service for model management and inference
    /// </summary>
    public class AIService : IAIService
    {
        private readonly string _modelsDirectory;
        private readonly string _adaptersDirectory;
        private readonly Dictionary<string, AIModel> _models = new();
        private readonly Dictionary<string, LoraAdapter> _adapters = new();
        private readonly SemaphoreSlim _initializationLock = new(1, 1);
        private readonly Lazy<ITokenizerService> _tokenizerService;
        private readonly ModelDriverRegistry _driverRegistry;
        private readonly IAILogger _logger;
        private bool _isInitialized = false;

        public event Action<IReadOnlyList<AIModel>>? ModelsUpdated;
        public event Action<IReadOnlyList<LoraAdapter>>? AdaptersUpdated;

        public AIService(IAILogger? logger = null)
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var mnemoAppPath = Path.Combine(appDataPath, "MnemoApp");
            _modelsDirectory = Path.Combine(mnemoAppPath, "models");
            _adaptersDirectory = Path.Combine(mnemoAppPath, "adapters");
            _tokenizerService = new Lazy<ITokenizerService>(() => new TokenizerService(this));
            _driverRegistry = new ModelDriverRegistry(logger);
            _logger = logger ?? new DebugAILogger();
        }

        public async Task InitializeAsync()
        {
            await _initializationLock.WaitAsync();
            try
            {
                if (_isInitialized)
                    return;

                // Ensure directories exist
                Directory.CreateDirectory(_modelsDirectory);
                Directory.CreateDirectory(_adaptersDirectory);

                // Discover models and adapters
                await DiscoverModelsAsync();
                await DiscoverAdaptersAsync();

                _isInitialized = true;
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        public async Task<IReadOnlyList<string>> GetAllNamesAsync()
        {
            await EnsureInitializedAsync();
            return _models.Keys.ToList().AsReadOnly();
        }

        public async Task<IReadOnlyList<AIModel>> GetAllModelsAsync()
        {
            await EnsureInitializedAsync();
            return _models.Values.ToList().AsReadOnly();
        }

        public async Task<AIModel?> GetModelAsync(string modelName)
        {
            await EnsureInitializedAsync();
            return _models.TryGetValue(modelName, out var model) ? model : null;
        }

        public async Task<bool> IsModelAvailableAsync(string modelName)
        {
            await EnsureInitializedAsync();
            return _models.ContainsKey(modelName);
        }

        public async Task<IReadOnlyList<LoraAdapter>> GetAvailableAdaptersAsync()
        {
            await EnsureInitializedAsync();
            return _adapters.Values.ToList().AsReadOnly();
        }

        public async Task<IReadOnlyList<LoraAdapter>> GetCompatibleAdaptersAsync(string modelName)
        {
            await EnsureInitializedAsync();
            
            // For now, return all adapters. In the future, you could implement
            // compatibility checking based on model architecture or metadata
            return _adapters.Values.ToList().AsReadOnly();
        }

        public async Task<AIInferenceResponse> InferAsync(AIInferenceRequest request, CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync();

            var model = await GetModelAsync(request.ModelName);
            if (model == null)
            {
                return new AIInferenceResponse
                {
                    Success = false,
                    ErrorMessage = $"Model '{request.ModelName}' not found"
                };
            }

            try
            {
                // Get or initialize the driver for this model
                var driver = _driverRegistry.GetInitializedDriver(request.ModelName);
                if (driver == null)
                {
                    // Preflight validation for clearer errors
                    if (model.Capabilities == null)
                    {
                        return new AIInferenceResponse { Success = false, ErrorMessage = $"Capabilities missing for model '{request.ModelName}'" };
                    }

                    // If OpenAI-compatible, require HttpEndpoint
                    if (model.Capabilities.OpenAiCompatible && string.IsNullOrWhiteSpace(model.Capabilities.HttpEndpoint))
                    {
                        return new AIInferenceResponse { Success = false, ErrorMessage = $"No HTTP endpoint configured for '{request.ModelName}' (openaiCompatible=true)" };
                    }

                    // If llama.cpp, require either HttpEndpoint or an executable hint
                    var hasExecType = !string.IsNullOrWhiteSpace(model.Capabilities.ExecutionType) && model.Capabilities.ExecutionType.Equals("llamacpp", StringComparison.OrdinalIgnoreCase);
                    if (hasExecType && string.IsNullOrWhiteSpace(model.Capabilities.HttpEndpoint))
                    {
                        var execCfg = model.Capabilities.ExecutionConfig;
                        var exe = execCfg != null && execCfg.TryGetValue("executable", out var exeObj) ? exeObj as string : null;
                        if (string.IsNullOrWhiteSpace(exe))
                        {
                            // Let the driver attempt environment/PATH-based resolution; continue to init
                        }
                    }

                    var initSuccess = await _driverRegistry.InitializeDriverForModel(model);
                    if (!initSuccess)
                    {
                        return new AIInferenceResponse
                        {
                            Success = false,
                            ErrorMessage = $"Failed to initialize driver for model '{request.ModelName}'"
                        };
                    }
                    driver = _driverRegistry.GetInitializedDriver(request.ModelName);
                }

                if (driver == null)
                {
                    return new AIInferenceResponse
                    {
                        Success = false,
                        ErrorMessage = $"No suitable driver found for model '{request.ModelName}'"
                    };
                }

                // Use the driver to perform inference
                return await driver.InferAsync(model, request, cancellationToken);
            }
            catch (Exception ex)
            {
                return new AIInferenceResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async IAsyncEnumerable<string> InferStreamAsync(AIInferenceRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync();

            var model = await GetModelAsync(request.ModelName);
            if (model == null)
            {
                yield return $"Error: Model '{request.ModelName}' not found";
                yield break;
            }

            // Get or initialize the driver for this model
            var driver = _driverRegistry.GetInitializedDriver(request.ModelName);
            if (driver == null)
            {
                var initSuccess = await _driverRegistry.InitializeDriverForModel(model);
                if (!initSuccess)
                {
                    yield return $"Error: Failed to initialize driver for model '{request.ModelName}'";
                    yield break;
                }
                driver = _driverRegistry.GetInitializedDriver(request.ModelName);
            }

            if (driver == null)
            {
                yield return $"Error: No suitable driver found for model '{request.ModelName}'";
                yield break;
            }

            // Use the driver to perform streaming inference
            await foreach (var token in driver.InferStreamAsync(model, request, cancellationToken))
            {
                yield return token;
            }
        }

        public async Task<TokenCountResult> CountTokensAsync(string text, string modelName)
        {
            await EnsureInitializedAsync();
            return await _tokenizerService.Value.CountTokensAsync(text, modelName);
        }

        public async Task RefreshModelsAsync()
        {
            await _initializationLock.WaitAsync();
            try
            {
                await DiscoverModelsAsync();
                await DiscoverAdaptersAsync();
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        private async Task EnsureInitializedAsync()
        {
            if (!_isInitialized)
                await InitializeAsync();
        }

        private async Task DiscoverModelsAsync()
        {
            _models.Clear();

            if (!Directory.Exists(_modelsDirectory))
                return;

            var modelDirectories = Directory.GetDirectories(_modelsDirectory);

            foreach (var modelDir in modelDirectories)
            {
                try
                {
                    var model = await LoadModelFromDirectoryAsync(modelDir);
                    if (model != null)
                    {
                        _models[model.Manifest.Name] = model;
                    }
                }
                catch (Exception ex)
                {
                    // Log error but continue with other models
                    _logger.LogWarning($"Failed to load model from {modelDir}: {ex.Message}");
                }
            }

            ModelsUpdated?.Invoke(_models.Values.ToList().AsReadOnly());
        }

        private async Task<AIModel?> LoadModelFromDirectoryAsync(string modelDirectory)
        {
            var manifestPath = Path.Combine(modelDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
                return null;

            var manifestJson = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<AIModelManifest>(manifestJson);
            if (manifest == null)
                return null;

            // Find the model file (.gguf) - optional for API-only models
            var modelFiles = Directory.GetFiles(modelDirectory, "*.gguf");
            string? modelFile = null;
            FileInfo? modelFileInfo = null;
            
            if (modelFiles.Length > 0)
            {
                modelFile = modelFiles[0];
                modelFileInfo = new FileInfo(modelFile);
            }

            // Load capabilities if available
            AIModelCapabilities? capabilities = null;
            var capabilitiesPath = Path.Combine(modelDirectory, "capabilities.json");
            if (File.Exists(capabilitiesPath))
            {
                try
                {
                    var capabilitiesJson = await File.ReadAllTextAsync(capabilitiesPath);
                    capabilities = JsonSerializer.Deserialize<AIModelCapabilities>(capabilitiesJson);
                    
                    // Basic validation
                    if (capabilities != null && !ValidateCapabilities(capabilities, modelDirectory))
                    {
                        System.Diagnostics.Debug.WriteLine($"Invalid capabilities.json for model in {modelDirectory}");
                        capabilities = null; // Reset to null if invalid
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to load capabilities.json for {modelDirectory}: {ex.Message}");
                    // If capabilities file is malformed, continue without it
                }
            }

            // For API-only models, require capabilities with endpoint info
            if (modelFile == null)
            {
                if (capabilities?.OpenAiCompatible != true || string.IsNullOrWhiteSpace(capabilities.HttpEndpoint))
                {
                    _logger.LogWarning($"API-only model in {modelDirectory} missing required capabilities (OpenAiCompatible=true + HttpEndpoint)");
                    return null; // API-only models must have proper capabilities
                }
            }

            // Check for tokenizer
            var tokenizerPath = Path.Combine(modelDirectory, "tokenizer.model");
            var hasTokenizer = File.Exists(tokenizerPath);

            // Get available LoRA adapters for this model
            var modelAdapters = await GetModelSpecificAdaptersAsync(Path.GetFileName(modelDirectory));

            return new AIModel
            {
                DirectoryPath = modelDirectory,
                ModelFileName = modelFile != null ? Path.GetFileName(modelFile) : "", // Empty for API-only models
                Manifest = manifest,
                Capabilities = capabilities,
                HasTokenizer = hasTokenizer,
                AvailableLoraAdapters = modelAdapters,
                LastModified = modelFileInfo?.LastWriteTime ?? Directory.GetLastWriteTime(modelDirectory),
                ModelFileSize = modelFileInfo?.Length ?? 0 // 0 for API-only models
            };
        }

        private Task<List<string>> GetModelSpecificAdaptersAsync(string modelName)
        {
            var adapters = new List<string>();
            var modelAdapterPath = Path.Combine(_adaptersDirectory, modelName);
            
            if (Directory.Exists(modelAdapterPath))
            {
                var adapterFiles = Directory.GetFiles(modelAdapterPath, "*.bin")
                    .Concat(Directory.GetFiles(modelAdapterPath, "*.safetensors"))
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Cast<string>();
                
                adapters.AddRange(adapterFiles);
            }

            return Task.FromResult(adapters);
        }

        public async ValueTask DisposeAsync()
        {
            await _driverRegistry.ShutdownAllAsync();
        }

        private Task DiscoverAdaptersAsync()
        {
            _adapters.Clear();

            if (!Directory.Exists(_adaptersDirectory))
                return Task.CompletedTask;

            var adapterFiles = Directory.GetFiles(_adaptersDirectory, "*.bin", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(_adaptersDirectory, "*.safetensors", SearchOption.AllDirectories));

            foreach (var adapterFile in adapterFiles)
            {
                try
                {
                    var adapter = CreateAdapterFromFile(adapterFile);
                    if (adapter != null)
                    {
                        _adapters[adapter.Name] = adapter;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to load adapter from {adapterFile}: {ex.Message}");
                }
            }

            AdaptersUpdated?.Invoke(_adapters.Values.ToList().AsReadOnly());
            return Task.CompletedTask;
        }

        private LoraAdapter? CreateAdapterFromFile(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            var name = Path.GetFileNameWithoutExtension(filePath);

            return new LoraAdapter
            {
                Name = name,
                FilePath = filePath,
                LastModified = fileInfo.LastWriteTime,
                FileSize = fileInfo.Length
            };
        }

        /// <summary>
        /// Basic validation of capabilities.json content
        /// </summary>
        private bool ValidateCapabilities(AIModelCapabilities capabilities, string modelDirectory)
        {
            try
            {
                // Check OpenAI-compatible models have endpoint
                if (capabilities.OpenAiCompatible && string.IsNullOrWhiteSpace(capabilities.HttpEndpoint))
                {
                    _logger.LogWarning("OpenAI-compatible model missing HttpEndpoint");
                    return false;
                }

                // Check llama.cpp models have either endpoint or executable config
                var isLlamaCpp = capabilities.ExecutionType?.Equals("llamacpp", StringComparison.OrdinalIgnoreCase) == true;
                if (isLlamaCpp && string.IsNullOrWhiteSpace(capabilities.HttpEndpoint))
                {
                    var hasExecutable = capabilities.ExecutionConfig?.ContainsKey("executable") == true;
                    if (!hasExecutable)
                    {
                        _logger.LogWarning("LlamaCpp model missing both HttpEndpoint and executable config");
                        return false;
                    }
                }

                // Validate context length is reasonable
                if (capabilities.MaxContextLength <= 0)
                {
                    _logger.LogWarning("Invalid MaxContextLength in capabilities");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Capabilities validation error: {ex.Message}", ex);
                return false;
            }
        }
    }
}
