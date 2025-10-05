using System;
using System.Threading;
using System.Threading.Tasks;
using MnemoApp.Core;
using MnemoApp.Core.AI.Services;
using MnemoApp.Core.AI.Models;
using MnemoApp.Core.Tasks.Models;

namespace MnemoApp.Core.Tasks.Examples
{
    /// <summary>
    /// Example task for AI-based generation that requires exclusive execution
    /// </summary>
    public class AIGenerationTask : MnemoTaskBase
    {
        private readonly IAIService _aiService;
        private readonly AIInferenceRequest _request;
        private readonly IModelSelectionService? _modelSelectionService;

        public AIGenerationTask(IAIService aiService, AIInferenceRequest request, string name, string? description = null, IModelSelectionService? modelSelectionService = null)
            : base(name, description, TaskPriority.High, TaskExecutionMode.Exclusive, usingAI: true)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
            _request = request ?? throw new ArgumentNullException(nameof(request));
            _modelSelectionService = modelSelectionService;
        }

        public override TimeSpan? EstimatedDuration => TimeSpan.FromMinutes(2); // Rough estimate

        protected override async Task<TaskResult> ExecuteTaskAsync(IProgress<TaskProgress> progress, CancellationToken cancellationToken)
        {
            try
            {
                // Resolve effective model name if caller passed a placeholder like "default"
                var effectiveModelName = _request.ModelName;
                if (string.IsNullOrWhiteSpace(effectiveModelName) || effectiveModelName.Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    effectiveModelName = _modelSelectionService?.SelectedModel;

                    if (string.IsNullOrWhiteSpace(effectiveModelName))
                    {
                        var names = await _aiService.GetAllNamesAsync();
                        if (names.Count > 0)
                        {
                            effectiveModelName = names[0];
                        }
                    }

                    if (string.IsNullOrWhiteSpace(effectiveModelName))
                    {
                        return new TaskResult(false, ErrorMessage: "No AI model selected or available");
                    }

                    _request.ModelName = effectiveModelName;
                }

                System.Diagnostics.Debug.WriteLine($"[AI_GENERATION] Starting AI generation task: '{Name}'");
                System.Diagnostics.Debug.WriteLine($"[AI_GENERATION] Model: {_request.ModelName}, MaxTokens: {_request.MaxTokens}");
                
                progress.Report(new TaskProgress(0.1, "Initializing AI model..."));
                await Task.Delay(500, cancellationToken); // Simulate initialization

                progress.Report(new TaskProgress(0.2, "Starting generation..."));
                System.Diagnostics.Debug.WriteLine($"[AI_GENERATION] Calling AI service for inference...");
                
                var response = await _aiService.InferAsync(_request, cancellationToken);
                
                if (!response.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"[AI_GENERATION] AI inference failed: {response.ErrorMessage}");
                    return new TaskResult(false, ErrorMessage: response.ErrorMessage ?? "AI inference failed");
                }

                progress.Report(new TaskProgress(0.9, "Finalizing..."));
                await Task.Delay(200, cancellationToken);

                progress.Report(new TaskProgress(1.0, "Generation complete"));
                System.Diagnostics.Debug.WriteLine($"[AI_GENERATION] AI generation completed successfully. Response length: {response.Response?.Length ?? 0} chars");
                
                return new TaskResult(true, response.Response);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"[AI_GENERATION] AI generation task cancelled: '{Name}'");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AI_GENERATION] AI generation task failed: '{Name}' - {ex.Message}");
                return new TaskResult(false, ErrorMessage: ex.Message);
            }
        }
    }

    /// <summary>
    /// Example task for parsing attachments that can run in parallel
    /// </summary>
    public class ParseAttachmentsTask : MnemoTaskBase
    {
        private readonly string[] _filePaths;

        public ParseAttachmentsTask(string[] filePaths)
            : base("Parsing Attachments", "Processing uploaded attachments", TaskPriority.Normal, TaskExecutionMode.Parallel)
        {
            _filePaths = filePaths ?? throw new ArgumentNullException(nameof(filePaths));
        }

        public override TimeSpan? EstimatedDuration => TimeSpan.FromSeconds(30);

        protected override async Task<TaskResult> ExecuteTaskAsync(IProgress<TaskProgress> progress, CancellationToken cancellationToken)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[PARSE_ATTACHMENTS] Starting file parsing task: '{Name}' - {_filePaths.Length} files");
                var results = new string[_filePaths.Length];
                
                for (int i = 0; i < _filePaths.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var progressValue = (double)i / _filePaths.Length;
                    var fileName = System.IO.Path.GetFileName(_filePaths[i]);
                    progress.Report(new TaskProgress(progressValue, $"Processing {fileName}..."));
                    
                    System.Diagnostics.Debug.WriteLine($"[PARSE_ATTACHMENTS] Processing file {i + 1}/{_filePaths.Length}: {fileName}");
                    
                    // Simulate file processing
                    await Task.Delay(1000, cancellationToken);
                    results[i] = $"Processed: {_filePaths[i]}";
                }

                progress.Report(new TaskProgress(1.0, "All attachments processed"));
                System.Diagnostics.Debug.WriteLine($"[PARSE_ATTACHMENTS] File parsing completed successfully - {_filePaths.Length} files processed");
                return new TaskResult(true, results);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"[PARSE_ATTACHMENTS] File parsing task cancelled: '{Name}'");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PARSE_ATTACHMENTS] File parsing task failed: '{Name}' - {ex.Message}");
                return new TaskResult(false, ErrorMessage: ex.Message);
            }
        }
    }

    /// <summary>
    /// Composite task for generating a learning path with multiple units
    /// </summary>
    public class GeneratePathTask : MnemoTaskBase
    {
        private readonly IAIService _aiService;
        private readonly string _pathTopic;
        private readonly int _unitCount;
        private readonly IModelSelectionService? _modelSelectionService;

        public GeneratePathTask(IAIService aiService, string pathTopic, int unitCount, IModelSelectionService? modelSelectionService = null)
            : base("Generate Learning Path", $"Creating {unitCount} units for '{pathTopic}'", TaskPriority.High, TaskExecutionMode.Exclusive, usingAI: true)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
            _pathTopic = pathTopic ?? throw new ArgumentNullException(nameof(pathTopic));
            _unitCount = unitCount;
            _modelSelectionService = modelSelectionService;
        }

        public override TimeSpan? EstimatedDuration => TimeSpan.FromMinutes(_unitCount * 2);

        protected override async Task<TaskResult> ExecuteTaskAsync(IProgress<TaskProgress> progress, CancellationToken cancellationToken)
        {
            try
            {
                progress.Report(new TaskProgress(0.05, "Planning learning path structure..."));
                await Task.Delay(1000, cancellationToken);

                var units = new string[_unitCount];
                
                // Resolve a concrete model once for the whole composite task
                string? ResolveSelectedModel = _modelSelectionService?.SelectedModel;
                
                if (string.IsNullOrWhiteSpace(ResolveSelectedModel))
                {
                    var names = await _aiService.GetAllNamesAsync();
                    if (names.Count > 0)
                    {
                        ResolveSelectedModel = names[0];
                    }
                }
                if (string.IsNullOrWhiteSpace(ResolveSelectedModel))
                {
                    return new TaskResult(false, ErrorMessage: "No AI model selected or available");
                }
                
                for (int i = 0; i < _unitCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var progressValue = 0.1 + (0.8 * i / _unitCount);
                    progress.Report(new TaskProgress(progressValue, $"Generating unit {i + 1} of {_unitCount}..."));
                    
                    var request = new AIInferenceRequest
                    {
                        ModelName = ResolveSelectedModel,
                        Prompt = $"Create unit {i + 1} for learning path on {_pathTopic}",
                        MaxTokens = 500
                    };
                    
                    var response = await _aiService.InferAsync(request, cancellationToken);
                    if (!response.Success)
                    {
                        return new TaskResult(false, ErrorMessage: $"Failed to generate unit {i + 1}: {response.ErrorMessage}");
                    }
                    
                    units[i] = response.Response ?? "Generated unit content";
                }

                progress.Report(new TaskProgress(0.95, "Finalizing learning path..."));
                await Task.Delay(500, cancellationToken);

                progress.Report(new TaskProgress(1.0, "Learning path generated successfully"));
                return new TaskResult(true, units);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new TaskResult(false, ErrorMessage: ex.Message);
            }
        }
    }
}
