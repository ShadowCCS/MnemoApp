using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using MnemoApp.Core.Tasks.Models;

namespace MnemoApp.Core.Tasks.Services
{
    public interface ITaskSchedulerService
    {
        /// <summary>
        /// All tasks in the system (active and completed)
        /// </summary>
        ReadOnlyObservableCollection<IMnemoTask> AllTasks { get; }

        /// <summary>
        /// Currently running tasks
        /// </summary>
        ReadOnlyObservableCollection<IMnemoTask> RunningTasks { get; }

        /// <summary>
        /// Tasks waiting to be executed
        /// </summary>
        ReadOnlyObservableCollection<IMnemoTask> PendingTasks { get; }

        /// <summary>
        /// Completed tasks
        /// </summary>
        ReadOnlyObservableCollection<IMnemoTask> CompletedTasks { get; }

        /// <summary>
        /// Maximum number of parallel tasks that can run simultaneously
        /// </summary>
        int MaxParallelTasks { get; set; }

        /// <summary>
        /// Whether the scheduler is currently running
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Schedule a task for execution
        /// </summary>
        /// <param name="task">Task to schedule</param>
        /// <returns>Task ID</returns>
        Guid ScheduleTask(IMnemoTask task);

        /// <summary>
        /// Get a task by ID
        /// </summary>
        IMnemoTask? GetTask(Guid taskId);

        /// <summary>
        /// Cancel a task
        /// </summary>
        Task<bool> CancelTaskAsync(Guid taskId);

        /// <summary>
        /// Pause a task
        /// </summary>
        Task<bool> PauseTaskAsync(Guid taskId);

        /// <summary>
        /// Resume a task
        /// </summary>
        Task<bool> ResumeTaskAsync(Guid taskId);

        /// <summary>
        /// Remove a completed task from the system
        /// </summary>
        bool RemoveTask(Guid taskId);

        /// <summary>
        /// Clear all completed tasks
        /// </summary>
        void ClearCompletedTasks();

        /// <summary>
        /// Start the task scheduler
        /// </summary>
        Task StartAsync();

        /// <summary>
        /// Stop the task scheduler
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// Get tasks by parent ID
        /// </summary>
        IEnumerable<IMnemoTask> GetSubTasks(Guid parentTaskId);

        /// <summary>
        /// Get the root task for a given task
        /// </summary>
        IMnemoTask? GetRootTask(Guid taskId);

        /// <summary>
        /// Events
        /// </summary>
        event EventHandler<TaskEventArgs>? TaskStarted;
        event EventHandler<TaskEventArgs>? TaskCompleted;
        event EventHandler<TaskEventArgs>? TaskFailed;
        event EventHandler<TaskEventArgs>? TaskCancelled;
        event EventHandler<TaskEventArgs>? TaskProgressChanged;
    }

    public class TaskEventArgs : EventArgs
    {
        public required IMnemoTask Task { get; init; }
        public TaskProgress? Progress { get; init; }
    }
}
