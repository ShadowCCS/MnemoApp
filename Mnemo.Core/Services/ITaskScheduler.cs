using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mnemo.Core.Enums;

namespace Mnemo.Core.Services;

/// <summary>
/// Manages the scheduling and execution of background tasks.
/// </summary>
public interface ITaskScheduler
{
    /// <summary>
    /// Schedules a task for execution.
    /// </summary>
    /// <param name="name">The display name of the task.</param>
    /// <param name="action">The action to execute.</param>
    /// <param name="mode">The execution mode (Parallel or Exclusive).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the scheduling operation.</returns>
    Task ScheduleTaskAsync(string name, Func<CancellationToken, Task> action, TaskExecutionMode mode = TaskExecutionMode.Parallel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets sub-tasks associated with a parent task.
    /// </summary>
    /// <param name="parentTaskId">The ID of the parent task.</param>
    /// <returns>A collection of sub-tasks.</returns>
    IEnumerable<IMnemoTask> GetSubTasks(string parentTaskId);
}


