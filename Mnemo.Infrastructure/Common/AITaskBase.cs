using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Infrastructure.Common;

public abstract class AITaskBase : IAITask
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public abstract string DisplayName { get; }
    
    private AITaskStatus _status = AITaskStatus.Pending;
    public AITaskStatus Status 
    { 
        get => _status;
        protected set => _status = value;
    }

    public double TotalProgress => Steps.Count == 0 ? 0 : Steps.Average(s => s.Progress);
    
    protected readonly List<IAITaskStep> _steps = new();
    public IReadOnlyList<IAITaskStep> Steps => _steps;

    private int _currentStepIndex = 0;
    public int CurrentStepIndex => _currentStepIndex;

    protected CancellationTokenSource? _cts;

    public virtual async Task<Result> RunAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Status = AITaskStatus.Running;

        try
        {
            for (; _currentStepIndex < _steps.Count; _currentStepIndex++)
            {
                if (Status == AITaskStatus.Paused || Status == AITaskStatus.Cancelled)
                    break;

                var step = _steps[_currentStepIndex];
                var result = await step.ExecuteAsync(_cts.Token);

                if (!result.IsSuccess)
                {
                    Status = AITaskStatus.Failed;
                    return result;
                }
            }

            if (Status == AITaskStatus.Running)
            {
                Status = AITaskStatus.Completed;
            }

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            Status = AITaskStatus.Cancelled;
            return Result.Failure("Task was cancelled.");
        }
        catch (Exception ex)
        {
            Status = AITaskStatus.Failed;
            return Result.Failure($"Task failed: {ex.Message}", ex);
        }
    }

    public void Pause()
    {
        if (Status == AITaskStatus.Running)
        {
            Status = AITaskStatus.Paused;
            _cts?.Cancel();
        }
    }

    public void Resume()
    {
        if (Status == AITaskStatus.Paused)
        {
            Status = AITaskStatus.Pending;
        }
    }

    public void Cancel()
    {
        Status = AITaskStatus.Cancelled;
        _cts?.Cancel();
    }

    public async Task<Result> RedoStepAsync(int stepIndex, CancellationToken ct)
    {
        if (stepIndex < 0 || stepIndex >= _steps.Count)
            return Result.Failure("Invalid step index.");

        _currentStepIndex = stepIndex;
        return await RunAsync(ct);
    }
}


