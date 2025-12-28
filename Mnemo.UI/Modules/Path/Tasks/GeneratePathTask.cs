using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly IAIOrchestrator _orchestrator;
    private readonly ILoggerService _logger;

    public override string DisplayName => $"Generating Path: {_topic}";

    public GeneratePathTask(string topic, IAIOrchestrator orchestrator, ILoggerService logger)
    {
        _topic = topic;
        _orchestrator = orchestrator;
        _logger = logger;

        // Add the initial step to generate the structure
        _steps.Add(new GenerateStructureStep(this));
    }

    public void AddUnitStep(string unitName)
    {
        _steps.Add(new GenerateUnitContentStep(this, unitName));
    }

    private class GenerateStructureStep : IAITaskStep
    {
        private readonly GeneratePathTask _parent;
        public string Id { get; } = Guid.NewGuid().ToString();
        public string DisplayName => "Generating Path Structure";
        public string Description => "Creating the units and modules for the learning path.";
        public AITaskStatus Status { get; private set; } = AITaskStatus.Pending;
        public double Progress { get; private set; } = 0;
        public string? ErrorMessage { get; private set; }

        public GenerateStructureStep(GeneratePathTask parent) => _parent = parent;

        public async Task<Result> ExecuteAsync(CancellationToken ct)
        {
            Status = AITaskStatus.Running;
            Progress = 0.5;

            var prompt = $"Generate a learning path structure for '{_parent._topic}' in JSON format. Return a JSON array of strings representing unit titles.";
            var result = await _parent._orchestrator.PromptAsync("You are a curriculum designer.", prompt, ct);

            if (result.IsSuccess)
            {
                // In a real implementation, we'd parse the JSON.
                // For the PoC, we'll pretend we got 3 units.
                _parent.AddUnitStep("Introduction");
                _parent.AddUnitStep("Core Concepts");
                _parent.AddUnitStep("Advanced Techniques");

                Progress = 1.0;
                Status = AITaskStatus.Completed;
                return Result.Success();
            }

            ErrorMessage = result.ErrorMessage;
            Status = AITaskStatus.Failed;
            return Result.Failure(result.ErrorMessage!);
        }
    }

    private class GenerateUnitContentStep : IAITaskStep
    {
        private readonly GeneratePathTask _parent;
        private readonly string _unitName;
        public string Id { get; } = Guid.NewGuid().ToString();
        public string DisplayName => $"Generating: {_unitName}";
        public string Description => $"Creating markdown content for the '{_unitName}' unit.";
        public AITaskStatus Status { get; private set; } = AITaskStatus.Pending;
        public double Progress { get; private set; } = 0;
        public string? ErrorMessage { get; private set; }

        public GenerateUnitContentStep(GeneratePathTask parent, string unitName)
        {
            _parent = parent;
            _unitName = unitName;
        }

        public async Task<Result> ExecuteAsync(CancellationToken ct)
        {
            Status = AITaskStatus.Running;
            Progress = 0.2;

            var prompt = $"Write a detailed markdown explanation for the unit '{_unitName}' as part of the '{_parent._topic}' learning path.";
            var result = await _parent._orchestrator.PromptAsync("You are a helpful tutor.", prompt, ct);

            if (result.IsSuccess)
            {
                Progress = 1.0;
                Status = AITaskStatus.Completed;
                return Result.Success();
            }

            ErrorMessage = result.ErrorMessage;
            Status = AITaskStatus.Failed;
            return Result.Failure(result.ErrorMessage!);
        }
    }
}


