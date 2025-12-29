using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
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
                _steps.Add(new GenerateUnitContentStep(this, _generatedPath.PathId, unit.UnitId));
            }
        }
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
                var searchResult = await _parent._knowledge.SearchAsync(searchQuery, 10, ct);
                var chunks = searchResult.IsSuccess ? searchResult.Value : Enumerable.Empty<KnowledgeChunk>();

                var contextBuilder = new StringBuilder();
                foreach (var chunk in chunks)
                {
                    contextBuilder.AppendLine($"--- Source: {chunk.SourceId} ---");
                    contextBuilder.AppendLine(chunk.Content);
                }

                Progress = 0.4;

                // 3. Prompt AI for structure
                var systemPrompt = @"You are an expert curriculum designer. 
Generate a comprehensive learning path in JSON format based on the provided topic, instructions, and source materials.
Follow the exact JSON template provided. Ensure each unit has a clear goal and allocated material chunks.
The output MUST be valid JSON only.";

                var userPrompt = $@"Topic: {_parent._topic}
Instructions: {_parent._instructions}

Source Materials:
{contextBuilder}

JSON Template:
{{
  ""title"": ""string"",
  ""description"": ""string"",
  ""difficulty"": ""beginner | intermediate | advanced"",
  ""estimated_time_hours"": number,
  ""units"": [
    {{
      ""order"": number,
      ""title"": ""string"",
      ""goal"": ""string"",
      ""allocated_material"": {{
        ""chunk_ids"": [""string""],
        ""summary"": ""string""
      }},
      ""generation_hints"": {{
        ""focus"": [""string""],
        ""avoid"": [""string""],
        ""prerequisites"": [""string""]
      }}
    }}
  ]
}}";

                var aiResult = await _parent._orchestrator.PromptAsync(systemPrompt, userPrompt, ct);
                if (!aiResult.IsSuccess) return Result.Failure(aiResult.ErrorMessage!);

                var json = aiResult.Value;
                
                // Robust JSON extraction from potential markdown
                int startIndex = json.IndexOf('{');
                int endIndex = json.LastIndexOf('}');
                
                if (startIndex != -1 && endIndex != -1 && endIndex > startIndex)
                {
                    json = json.Substring(startIndex, endIndex - startIndex + 1);
                }

                _parent._generatedPath = JsonSerializer.Deserialize<LearningPath>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
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
        public string Description { get; private set; } = "Creating detailed content.";
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
                var contextBuilder = new StringBuilder();
                if (unit.AllocatedMaterial.ChunkIds.Count > 0)
                {
                    // For now we might not have the chunks directly by ID if they weren't stored in the model with content.
                    // But we can search again specifically for this unit's goal.
                    var searchResult = await _parent._knowledge.SearchAsync($"{unit.Title} {unit.Goal}", 5, ct);
                    if (searchResult.IsSuccess)
                    {
                        foreach (var chunk in searchResult.Value)
                        {
                            contextBuilder.AppendLine(chunk.Content);
                        }
                    }
                }

                Progress = 0.3;

                // 2. Prompt for content
                var systemPrompt = @"You are a friendly, patient, and encouraging tutor. 
Generate educational content for the specific unit following these rules:
1. Unit Title (Clear, learner-friendly)
2. Why This Topic Matters (Relevance before definitions, real-world intuition)
3. Conceptual Introduction (Informal, metaphors, no formulas yet)
4. Formal Explanation (Definitions, LaTeX for math if needed)
5. Interpretation & Understanding (What it means, address confusion)
6. What to Remember (Short recap, intuition focus)

Tone: Assume learner is capable but new. Never shame. 
Formatting: Markdown headings, short paragraphs, LaTeX only when needed. Whitespace is key.
Avoid: Tool instructions, academic-only language, dense formula blocks without explanation.";

                var userPrompt = $@"Learning Path: {path.Title}
Current Unit: {unit.Title}
Goal: {unit.Goal}
Focus: {string.Join(", ", unit.GenerationHints.Focus)}
Avoid: {string.Join(", ", unit.GenerationHints.Avoid)}
Prerequisites: {string.Join(", ", unit.GenerationHints.Prerequisites)}

Source Material Context:
{contextBuilder}";

                var aiResult = await _parent._orchestrator.PromptAsync(systemPrompt, userPrompt, ct);
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
                return Result.Failure(ex.Message, ex);
            }
        }
    }
}
