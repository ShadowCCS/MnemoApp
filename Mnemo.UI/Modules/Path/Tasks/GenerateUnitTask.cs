using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.Infrastructure.Common;

namespace Mnemo.UI.Modules.Path.Tasks;

public class GenerateUnitTask : AITaskBase
{
    private readonly string _pathId;
    private readonly string _unitId;
    private readonly IAIOrchestrator _orchestrator;
    private readonly IKnowledgeService _knowledge;
    private readonly ILearningPathService _pathService;
    private readonly ILoggerService _logger;

    public override string DisplayName => "Generating Learning Unit";

    public GenerateUnitTask(
        string pathId,
        string unitId,
        IAIOrchestrator orchestrator,
        IKnowledgeService knowledge,
        ILearningPathService pathService,
        ILoggerService logger)
    {
        _pathId = pathId;
        _unitId = unitId;
        _orchestrator = orchestrator;
        _knowledge = knowledge;
        _pathService = pathService;
        _logger = logger;

        _steps.Add(new GenerateUnitStep(this));
    }

    private class GenerateUnitStep : IAITaskStep
    {
        private readonly GenerateUnitTask _parent;
        public string Id { get; } = Guid.NewGuid().ToString();
        public string DisplayName { get; private set; } = "Generating Unit";
        public string Description { get; private set; } = "Creating detailed content.";
        public AITaskStatus Status { get; private set; } = AITaskStatus.Pending;
        public double Progress { get; private set; } = 0;
        public string? ErrorMessage { get; private set; }

        public GenerateUnitStep(GenerateUnitTask parent) => _parent = parent;

        public async Task<Result> ExecuteAsync(CancellationToken ct)
        {
            Status = AITaskStatus.Running;
            Progress = 0.1;

            try
            {
                var path = await _parent._pathService.GetPathAsync(_parent._pathId);
                var unit = path?.Units.FirstOrDefault(u => u.UnitId == _parent._unitId);

                if (path == null || unit == null) return Result.Failure("Unit or Path not found.");

                DisplayName = $"Generating: {unit.Title}";
                Description = unit.Goal;

                // 1. Gather material for this unit
                var contextBuilder = new StringBuilder();
                var searchResult = await _parent._knowledge.SearchAsync($"{unit.Title} {unit.Goal}", 5, ct);
                if (searchResult.IsSuccess)
                {
                    foreach (var chunk in searchResult.Value)
                    {
                        contextBuilder.AppendLine(chunk.Content);
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
                
                // Try to update unit status to failed if possible
                try 
                {
                    var path = await _parent._pathService.GetPathAsync(_parent._pathId);
                    var unit = path?.Units.FirstOrDefault(u => u.UnitId == _parent._unitId);
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

