using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using MnemoApp.Core.Tasks.Models;
using MnemoApp.Core.Tasks.Services;
using MnemoApp.Core.Overlays;

namespace MnemoApp.Core.Services
{
    /// <summary>
    /// Service that integrates task system with toast notifications
    /// </summary>
    public class TaskToastService : IDisposable
    {
        private readonly IToastService _toastService;
        private readonly ITaskSchedulerService _taskScheduler;
        private readonly IOverlayService _overlayService;
        private readonly ConcurrentDictionary<Guid, Guid> _taskToToastMap = new();
        private readonly ConcurrentDictionary<Guid, IDisposable> _taskSubscriptions = new();

        public TaskToastService(IToastService toastService, ITaskSchedulerService taskScheduler, IOverlayService overlayService)
        {
            _toastService = toastService ?? throw new ArgumentNullException(nameof(toastService));
            _taskScheduler = taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler));
            _overlayService = overlayService ?? throw new ArgumentNullException(nameof(overlayService));

            // Subscribe to task scheduler events
            _taskScheduler.TaskStarted += OnTaskStarted;
            _taskScheduler.TaskCompleted += OnTaskCompleted;
            _taskScheduler.TaskFailed += OnTaskFailed;
            _taskScheduler.TaskCancelled += OnTaskCancelled;
            _taskScheduler.TaskProgressChanged += OnTaskProgressChanged;
        }

        /// <summary>
        /// Create a toast notification for a task
        /// </summary>
        public Guid CreateTaskToast(IMnemoTask task, bool showProgress = true)
        {
            // Use lock to ensure thread safety during rapid task creation
            lock (_taskSubscriptions)
            {
                // Check if toast already exists for this task
                if (_taskToToastMap.TryGetValue(task.Id, out var existingToastId))
                {
                    // Update existing toast instead of creating a new one
                    UpdateTaskToast(task);
                    return existingToastId;
                }

                var toastType = GetToastTypeForTask(task);
                var title = task.Name;
                var message = GetTaskMessage(task);
                var progress = showProgress ? task.Progress : (double?)null;
                var progressText = showProgress ? task.ProgressText : null;

                var toastId = _toastService.ShowStatus(title, message, toastType, true, progress, progressText);
                _taskToToastMap[task.Id] = toastId;

                // Subscribe to task property changes
                var subscription = new TaskPropertyChangedSubscription(task, (sender, e) => OnTaskPropertyChanged(task, e));
                _taskSubscriptions[task.Id] = subscription;

                return toastId;
            }
        }

        /// <summary>
        /// Update toast for a task based on current task state
        /// </summary>
        public bool UpdateTaskToast(IMnemoTask task)
        {
            // Check if task still exists in our mapping (it might have been removed)
            if (!_taskToToastMap.TryGetValue(task.Id, out var toastId))
                return false;

            var toastType = GetToastTypeForTask(task);
            return _toastService.TryUpdateStatus(toastId,
                progress: task.Progress,
                progressText: task.ProgressText,
                title: task.Name,
                message: GetTaskMessage(task),
                type: toastType);
        }

        /// <summary>
        /// Remove toast for a task
        /// </summary>
        public bool RemoveTaskToast(Guid taskId)
        {
            IDisposable? subscription = null;
            Guid toastId;

            // Use lock to ensure thread safety during removal
            lock (_taskSubscriptions)
            {
                if (_taskToToastMap.TryRemove(taskId, out toastId))
                {
                    _taskSubscriptions.TryRemove(taskId, out subscription);
                }
                else
                {
                    return false;
                }
            }

            // Dispose subscription outside the lock to avoid holding lock during disposal
            subscription?.Dispose();

            return _toastService.RemoveStatus(toastId);
        }

        /// <summary>
        /// Get toast ID for a task
        /// </summary>
        public Guid? GetToastIdForTask(Guid taskId)
        {
            return _taskToToastMap.TryGetValue(taskId, out var toastId) ? toastId : null;
        }

        /// <summary>
        /// Show loading overlay when toast is clicked
        /// </summary>
        public void EnableToastClickToOverlay()
        {
            // This would be implemented by adding click handlers to toast notifications
            // For now, this is a placeholder for the integration
        }

        private void OnTaskStarted(object? sender, TaskEventArgs e)
        {
            // Avoid duplicate toasts if one was already created explicitly
            if (_taskToToastMap.ContainsKey(e.Task.Id))
            {
                UpdateTaskToast(e.Task);
                return;
            }
            CreateTaskToast(e.Task);
        }

        private void OnTaskCompleted(object? sender, TaskEventArgs e)
        {
            UpdateTaskToast(e.Task);

            // Schedule toast removal after a short delay
            // Use a single timer to avoid accumulating many pending tasks
            ScheduleToastRemoval(e.Task.Id, TimeSpan.FromSeconds(3));
        }

        private void OnTaskFailed(object? sender, TaskEventArgs e)
        {
            UpdateTaskToast(e.Task);

            // Schedule toast removal after a longer delay for failed tasks
            ScheduleToastRemoval(e.Task.Id, TimeSpan.FromSeconds(10));
        }

        private void OnTaskCancelled(object? sender, TaskEventArgs e)
        {
            RemoveTaskToast(e.Task.Id);
        }

        private void OnTaskProgressChanged(object? sender, TaskEventArgs e)
        {
            UpdateTaskToast(e.Task);
        }

        private void OnTaskPropertyChanged(IMnemoTask task, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IMnemoTask.Status) ||
                e.PropertyName == nameof(IMnemoTask.Progress) ||
                e.PropertyName == nameof(IMnemoTask.ProgressText) ||
                e.PropertyName == nameof(IMnemoTask.Name) ||
                e.PropertyName == nameof(IMnemoTask.Description))
            {
                // Throttle toast updates to avoid overwhelming the UI thread
                // Only update for significant status changes or progress milestones
                if (e.PropertyName == nameof(IMnemoTask.Status) ||
                    (e.PropertyName == nameof(IMnemoTask.Progress) && task.Progress % 10 < 1) || // Update every 10%
                    e.PropertyName == nameof(IMnemoTask.ProgressText))
                {
                    UpdateTaskToast(task);
                }
            }
        }

        private static ToastType GetToastTypeForTask(IMnemoTask task)
        {
            return task.Status switch
            {
                MnemoApp.Core.Tasks.Models.TaskStatus.Pending => ToastType.Info,
                MnemoApp.Core.Tasks.Models.TaskStatus.WaitingOnQueue => ToastType.Info,
                MnemoApp.Core.Tasks.Models.TaskStatus.Running => ToastType.Process,
                MnemoApp.Core.Tasks.Models.TaskStatus.Completed => ToastType.Success,
                MnemoApp.Core.Tasks.Models.TaskStatus.Failed => ToastType.Error,
                MnemoApp.Core.Tasks.Models.TaskStatus.Cancelled => ToastType.Warning,
                MnemoApp.Core.Tasks.Models.TaskStatus.Paused => ToastType.Warning,
                _ => ToastType.Info
            };
        }

        private static string GetTaskMessage(IMnemoTask task)
        {
            // Show "Waiting on queue" for tasks that are waiting (either explicitly marked or in pending state)
            if (task.Status == MnemoApp.Core.Tasks.Models.TaskStatus.WaitingOnQueue ||
                task.Status == MnemoApp.Core.Tasks.Models.TaskStatus.Pending)
            {
                return $"Waiting on queue - {task.Description ?? "Task is waiting for available resources"}";
            }
            return task.Description ?? "Processing...";
        }

        private void ScheduleToastRemoval(Guid taskId, TimeSpan delay)
        {
            // Use Task.Run to avoid blocking and schedule the removal
            _ = Task.Run(async () =>
            {
                await Task.Delay(delay);
                RemoveTaskToast(taskId);
            });
        }

        public void Dispose()
        {
            // Unsubscribe from task scheduler events
            _taskScheduler.TaskStarted -= OnTaskStarted;
            _taskScheduler.TaskCompleted -= OnTaskCompleted;
            _taskScheduler.TaskFailed -= OnTaskFailed;
            _taskScheduler.TaskCancelled -= OnTaskCancelled;
            _taskScheduler.TaskProgressChanged -= OnTaskProgressChanged;

            // Create a copy of subscriptions to dispose outside the lock
            var subscriptionsToDispose = new List<IDisposable>();

            lock (_taskSubscriptions)
            {
                subscriptionsToDispose.AddRange(_taskSubscriptions.Values);
                _taskSubscriptions.Clear();
                _taskToToastMap.Clear();
            }

            // Dispose subscriptions outside the lock
            foreach (var subscription in subscriptionsToDispose)
            {
                subscription.Dispose();
            }
        }
    }

    /// <summary>
    /// Helper class to manage property changed subscriptions for tasks
    /// </summary>
    internal class TaskPropertyChangedSubscription : IDisposable
    {
        private readonly IMnemoTask _task;
        private readonly PropertyChangedEventHandler _handler;

        public TaskPropertyChangedSubscription(IMnemoTask task, PropertyChangedEventHandler handler)
        {
            _task = task;
            _handler = handler;
            _task.PropertyChanged += _handler;
        }

        public void Dispose()
        {
            _task.PropertyChanged -= _handler;
        }
    }
}
