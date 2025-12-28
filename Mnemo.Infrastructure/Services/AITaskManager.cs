using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

public class AITaskManager : IAITaskManager
{
    private readonly ConcurrentDictionary<string, IAITask> _tasks = new();
    private readonly ConcurrentQueue<IAITask> _queue = new();
    private readonly ILoggerService _logger;
    private bool _isProcessing = false;

    public event Action<IAITask>? TaskUpdated;

    public AITaskManager(ILoggerService logger)
    {
        _logger = logger;
    }

    public Task<string> QueueTaskAsync(IAITask task)
    {
        _tasks[task.Id] = task;
        _queue.Enqueue(task);
        _logger.Info("AITaskManager", $"Task queued: {task.DisplayName} ({task.Id})");
        
        TaskUpdated?.Invoke(task);
        
        _ = ProcessQueueAsync(); // Fire and forget
        
        return Task.FromResult(task.Id);
    }

    public IAITask? GetTask(string taskId) => _tasks.TryGetValue(taskId, out var task) ? task : null;

    public IEnumerable<IAITask> GetAllTasks() => _tasks.Values;

    private async Task ProcessQueueAsync()
    {
        if (_isProcessing) return;
        _isProcessing = true;

        try
        {
            while (_queue.TryPeek(out var task))
            {
                if (task.Status == AITaskStatus.Pending || task.Status == AITaskStatus.Paused)
                {
                    _logger.Info("AITaskManager", $"Starting task: {task.DisplayName}");
                    await task.RunAsync(default).ConfigureAwait(false);
                    TaskUpdated?.Invoke(task);
                }

                if (task.Status == AITaskStatus.Completed || task.Status == AITaskStatus.Cancelled || task.Status == AITaskStatus.Failed)
                {
                    _queue.TryDequeue(out _);
                }
                else if (task.Status == AITaskStatus.Paused)
                {
                    // If paused, we might want to move it to the end of the queue or just stop processing for now
                    break;
                }
            }
        }
        finally
        {
            _isProcessing = false;
        }
    }
}

