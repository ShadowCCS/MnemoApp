using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Enums;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

public class TaskSchedulerService : ITaskScheduler
{
    private readonly ILoggerService _logger;
    private readonly SemaphoreSlim _exclusiveLock = new(1, 1);
    private readonly ConcurrentDictionary<Task, byte> _runningTasks = new();

    public TaskSchedulerService(ILoggerService logger)
    {
        _logger = logger;
    }

    public async Task ScheduleTaskAsync(string name, Func<CancellationToken, Task> action, TaskExecutionMode mode = TaskExecutionMode.Parallel, CancellationToken cancellationToken = default)
    {
        _logger.Info("TaskScheduler", $"Scheduling task: {name} (Mode: {mode})");

        if (mode == TaskExecutionMode.Exclusive)
        {
            await ExecuteExclusiveTaskAsync(name, action, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            ExecuteParallelTask(name, action, cancellationToken);
        }
    }

    private async Task ExecuteExclusiveTaskAsync(string name, Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await _exclusiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.Info("TaskScheduler", $"Starting exclusive task: {name}");
            // In a more complex implementation, we might want to pause or wait for parallel tasks
            // but for now we just ensure only one exclusive task runs at a time.
            await action(cancellationToken).ConfigureAwait(false);
            _logger.Info("TaskScheduler", $"Finished exclusive task: {name}");
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("TaskScheduler", $"Exclusive task cancelled: {name}");
        }
        catch (Exception ex)
        {
            _logger.Error("TaskScheduler", $"Error in exclusive task: {name}", ex);
        }
        finally
        {
            _exclusiveLock.Release();
        }
    }

    private void ExecuteParallelTask(string name, Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        var task = Task.Run(async () =>
        {
            try
            {
                _logger.Info("TaskScheduler", $"Starting parallel task: {name}");
                await action(cancellationToken).ConfigureAwait(false);
                _logger.Info("TaskScheduler", $"Finished parallel task: {name}");
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("TaskScheduler", $"Parallel task cancelled: {name}");
            }
            catch (Exception ex)
            {
                _logger.Error("TaskScheduler", $"Error in parallel task: {name}", ex);
            }
        }, cancellationToken);

        _runningTasks.TryAdd(task, 0);
        _ = task.ContinueWith(t => _runningTasks.TryRemove(t, out _), TaskContinuationOptions.ExecuteSynchronously);
    }

    public IEnumerable<IMnemoTask> GetSubTasks(string parentTaskId)
    {
        return Array.Empty<IMnemoTask>();
    }
}

