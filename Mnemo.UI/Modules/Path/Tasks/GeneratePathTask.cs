using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Schemas;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Common;

namespace Mnemo.UI.Modules.Path.Tasks;

public class GeneratePathTask : AITaskBase
{
    private readonly string _topic;
    private readonly string _instructions;
    private readonly string[] _filePaths;
    private readonly IAIOrchestrator _orchestrator;
    private readonly IKnowledgeService _knowledge;
    private readonly ILearningPathService _pathService;
    private readonly ISettingsService _settings;
    private readonly ILoggerService _logger;

    private LearningPath? _generatedPath;
    public LearningPath? GeneratedPath => _generatedPath;

    public override string DisplayName => $"Generating Learning Path: {_topic}";

    public GeneratePathTask(
        string topic, 
        string instructions, 
        string[] filePaths,
        IAIOrchestrator orchestrator, 
        IKnowledgeService knowledge,
        ILearningPathService pathService,
        ISettingsService settings,
        ILoggerService logger)
    {
        _topic = topic;
        _instructions = instructions;
        _filePaths = filePaths;
        _orchestrator = orchestrator;
        _knowledge = knowledge;
        _pathService = pathService;
        _settings = settings;
        _logger = logger;

        _steps.Add(new GenerateStructureStep(this));
    }

    public async Task AddUnitGenerationStepsAsync()
    {
        if (_generatedPath == null) return;

        bool smartGen = await _settings.GetAsync("AI.SmartUnitGeneration", false);
        
        var unitsToGenerate = smartGen ? _generatedPath.Units.Take(1) : _generatedPath.Units;

        foreach (var unit in unitsToGenerate)
        {
            if (unit.Status != AITaskStatus.Completed)
            {
                unit.Status = AITaskStatus.Running;
                _steps.Add(new GenerateUnitContentStep(this, _generatedPath.PathId, unit.UnitId));
            }
        }

        await _pathService.SavePathAsync(_generatedPath);
    }

    private class GenerateStructureStep : IAITaskStep
    {
        private readonly GeneratePathTask _parent;
        public string Id { get; } = Guid.NewGuid().ToString();
        public string DisplayName => "Designing Path Structure";
        public string Description => "Analyzing materials and creating units.";
        public AITaskStatus Status { get; private set; } = AITaskStatus.Pending;
        public double Progress { get; private set; } = 0;
        public string? ErrorMessage { get; private set; }

        public GenerateStructureStep(GeneratePathTask parent) => _parent = parent;

        private static string ExtractJson(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            // 1. Try to find markdown JSON block (case-insensitive for language tag)
            var match = System.Text.RegularExpressions.Regex.Match(
                input, 
                @"```(?:json)?\s*([\s\S]*?)\s*```", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var extracted = match.Groups[1].Value.Trim();
                // Ensure we extracted actual JSON content, not empty
                if (!string.IsNullOrEmpty(extracted) && extracted.StartsWith("{"))
                {
                    return extracted;
                }
            }

            // 2. Fallback to finding first { and last }
            int startIndex = input.IndexOf('{');
            int endIndex = input.LastIndexOf('}');

            if (startIndex != -1 && endIndex != -1 && endIndex > startIndex)
            {
                return input.Substring(startIndex, endIndex - startIndex + 1).Trim();
            }

            return input.Trim();
        }

        public async Task<Result> ExecuteAsync(CancellationToken ct)
        {
            Status = AITaskStatus.Running;
            Progress = 0.1;

            try 
            {
                // 1. Ingest files
                if (_parent._filePaths.Length > 0)
                {
                    double stepSize = 0.2 / _parent._filePaths.Length;
                    foreach (var file in _parent._filePaths)
                    {
                        var ingestResult = await _parent._knowledge.IngestDocumentAsync(file, ct);
                        if (!ingestResult.IsSuccess)
                        {
                            _parent._logger.Warning("PathGen", $"Failed to ingest {file}: {ingestResult.ErrorMessage}");
                        }
                        Progress += stepSize;
                    }
                }

                Progress = 0.3;

                // 2. RAG Search
                var searchQuery = $"{_parent._topic} {_parent._instructions}";
                var searchResult = await _parent._knowledge.SearchAsync(searchQuery, 15, ct);
                var chunks = searchResult.IsSuccess && searchResult.Value != null ? searchResult.Value : Enumerable.Empty<KnowledgeChunk>();

                Progress = 0.4;

                // 3. Prompt AI for structure (output shape is enforced by LearningPathJsonSchema via response_format).
                // Note: The model does not see the schema—only the prompt. Title length rules must be stated here (see llama.cpp grammars README).
                var systemPrompt = @"You are an expert curriculum designer. Generate a comprehensive learning path. Respond only with a JSON object. No conversational text before or after. Use forward slashes for any paths in strings.

CRITICAL title rules (follow exactly):
- Learning path ""title"": must be short, clean and concise — maximum 4 words (e.g. ""Introduction to Python"" or ""Data Structures Basics"").
- Each unit ""title"": maximum 3–5 words, short and clear (e.g. ""Variables and Types"", ""First Program"").";

                var userPrompt = $@"Create a learning path for the topic: '{_parent._topic}'
Additional instructions: {_parent._instructions}";

                var aiResult = await _parent._orchestrator.PromptWithContextAsync(systemPrompt, userPrompt, chunks, ct, LearningPathJsonSchema.GetSchema());
                if (!aiResult.IsSuccess || aiResult.Value == null) return Result.Failure(aiResult.ErrorMessage ?? "AI failed to respond.");

                var json = ExtractJson(aiResult.Value);
                
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                try
                {
                    _parent._generatedPath = JsonSerializer.Deserialize<LearningPath>(json, jsonOptions);
                }
                catch (JsonException ex)
                {
                    _parent._logger.Error("PathGen", $"JSON Parsing failed. Raw: {aiResult.Value}");
                    return Result.Failure($"Failed to parse generated path structure: {ex.Message}. The AI response was not valid JSON.", ex);
                }
                
                if (_parent._generatedPath == null) return Result.Failure("Failed to parse generated path structure.");

                _parent._generatedPath.Title = string.IsNullOrWhiteSpace(_parent._generatedPath.Title) ? _parent._topic : _parent._generatedPath.Title;
                _parent._generatedPath.Metadata.Model = "AI Assistant";

                // Save initial path
                await _parent._pathService.SavePathAsync(_parent._generatedPath);

                // Add unit steps
                await _parent.AddUnitGenerationStepsAsync();

                Progress = 1.0;
                Status = AITaskStatus.Completed;
                return Result.Success();
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                Status = AITaskStatus.Failed;
                return Result.Failure(ex.Message, ex);
            }
        }
    }

    private class GenerateUnitContentStep : IAITaskStep
    {
        private readonly GeneratePathTask _parent;
        private readonly string _pathId;
        private readonly string _unitId;
        public string Id { get; } = Guid.NewGuid().ToString();
        public string DisplayName { get; private set; } = "Generating Unit";
        public string Description { get; private set; } = "Creating unit content.";
        public AITaskStatus Status { get; private set; } = AITaskStatus.Pending;
        public double Progress { get; private set; } = 0;
        public string? ErrorMessage { get; private set; }

        public GenerateUnitContentStep(GeneratePathTask parent, string pathId, string unitId)
        {
            _parent = parent;
            _pathId = pathId;
            _unitId = unitId;
        }

        public async Task<Result> ExecuteAsync(CancellationToken ct)
        {
            Status = AITaskStatus.Running;
            Progress = 0.1;

            try
            {
                var path = await _parent._pathService.GetPathAsync(_pathId);
                var unit = path?.Units.FirstOrDefault(u => u.UnitId == _unitId);

                if (path == null || unit == null) return Result.Failure("Unit or Path not found.");

                DisplayName = $"Generating: {unit.Title}";
                Description = unit.Goal;

                // 1. Gather material for this unit
                var chunks = new List<KnowledgeChunk>();
                if (unit.AllocatedMaterial.ChunkIds.Count > 0)
                {
                    // For now we might not have the chunks directly by ID if they weren't stored in the model with content.
                    // But we can search again specifically for this unit's goal.
                    var searchResult = await _parent._knowledge.SearchAsync($"{unit.Title} {unit.Goal}", 5, ct);
                    if (searchResult.IsSuccess && searchResult.Value != null)
                    {
                        chunks.AddRange(searchResult.Value);
                    }
                }

                Progress = 0.3;

                // 2. Prompt for content
                var systemPrompt = @"You are a friendly, patient, and encouraging tutor. 
Generate educational content for the specific unit following these rules:
1. Why This Topic Matters (Relevance before definitions, real-world intuition)
2. Conceptual Introduction (Informal, metaphors, no formulas yet)
3. Formal Explanation (Definitions, LaTeX for math if needed)
4. Interpretation & Understanding (What it means, address confusion)
5. What to Remember (Short recap, intuition focus)

Tone: Assume learner is capable but new. Never shame. 
Formatting: Markdown headings, short paragraphs, LaTeX only when needed. Whitespace is key.
Avoid: Tool instructions, academic-only language, dense formula blocks without explanation, never include a title or heading at the top of your response.";

                var userPrompt = $@"Learning Path: {path.Title}
Current Unit: {unit.Title}
Goal: {unit.Goal}
Focus: {string.Join(", ", unit.GenerationHints.Focus)}
Avoid: {string.Join(", ", unit.GenerationHints.Avoid)}
Prerequisites: {string.Join(", ", unit.GenerationHints.Prerequisites)}";

                var aiResult = await _parent._orchestrator.PromptWithContextAsync(systemPrompt, userPrompt, chunks, ct);
                if (!aiResult.IsSuccess) return Result.Failure(aiResult.ErrorMessage!);

                unit.Content = aiResult.Value;
                unit.IsCompleted = true;
                unit.Status = AITaskStatus.Completed;

                await _parent._pathService.SavePathAsync(path);

                Progress = 1.0;
                Status = AITaskStatus.Completed;
                return Result.Success();
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                Status = AITaskStatus.Failed;

                try
                {
                    var path = await _parent._pathService.GetPathAsync(_pathId);
                    var unit = path?.Units.FirstOrDefault(u => u.UnitId == _unitId);
                    if (unit != null)
                    {
                        unit.Status = AITaskStatus.Failed;
                        await _parent._pathService.SavePathAsync(path!);
                    }
                }
                catch { /* Ignore secondary errors */ }

                return Result.Failure(ex.Message, ex);
            }
        }
    }
}
