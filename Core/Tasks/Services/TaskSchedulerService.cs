using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using MnemoApp.Core.Tasks.Models;

namespace MnemoApp.Core.Tasks.Services
{
    public class TaskSchedulerService : ITaskSchedulerService
    {
        private readonly ObservableCollection<IMnemoTask> _allTasks = new();
        private readonly ObservableCollection<IMnemoTask> _runningTasks = new();
        private readonly ObservableCollection<IMnemoTask> _pendingTasks = new();
        private readonly ObservableCollection<IMnemoTask> _completedTasks = new();

        private readonly ReadOnlyObservableCollection<IMnemoTask> _readOnlyAllTasks;
        private readonly ReadOnlyObservableCollection<IMnemoTask> _readOnlyRunningTasks;
        private readonly ReadOnlyObservableCollection<IMnemoTask> _readOnlyPendingTasks;
        private readonly ReadOnlyObservableCollection<IMnemoTask> _readOnlyCompletedTasks;

        private readonly ConcurrentDictionary<Guid, IMnemoTask> _taskLookup = new();
        private readonly SemaphoreSlim _exclusiveTaskSemaphore = new(1, 1);
        private readonly Timer _schedulerTimer;
        private readonly object _lockObject = new();
        private volatile bool _processingQueue;

        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isRunning;
        private int _maxParallelTasks = Environment.ProcessorCount;

        public TaskSchedulerService()
        {
            _readOnlyAllTasks = new ReadOnlyObservableCollection<IMnemoTask>(_allTasks);
            _readOnlyRunningTasks = new ReadOnlyObservableCollection<IMnemoTask>(_runningTasks);
            _readOnlyPendingTasks = new ReadOnlyObservableCollection<IMnemoTask>(_pendingTasks);
            _readOnlyCompletedTasks = new ReadOnlyObservableCollection<IMnemoTask>(_completedTasks);

            _schedulerTimer = new Timer(ProcessQueue, null, Timeout.Infinite, Timeout.Infinite);
        }

        public ReadOnlyObservableCollection<IMnemoTask> AllTasks => _readOnlyAllTasks;
        public ReadOnlyObservableCollection<IMnemoTask> RunningTasks => _readOnlyRunningTasks;
        public ReadOnlyObservableCollection<IMnemoTask> PendingTasks => _readOnlyPendingTasks;
        public ReadOnlyObservableCollection<IMnemoTask> CompletedTasks => _readOnlyCompletedTasks;

        public int MaxParallelTasks
        {
            get => _maxParallelTasks;
            set => _maxParallelTasks = Math.Max(1, value);
        }

        public bool IsRunning => _isRunning;

        public event EventHandler<TaskEventArgs>? TaskStarted;
        public event EventHandler<TaskEventArgs>? TaskCompleted;
        public event EventHandler<TaskEventArgs>? TaskFailed;
        public event EventHandler<TaskEventArgs>? TaskCancelled;
        public event EventHandler<TaskEventArgs>? TaskProgressChanged;

        public Guid ScheduleTask(IMnemoTask task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            System.Diagnostics.Debug.WriteLine($"[TASK_SCHEDULER] Scheduling task: '{task.Name}' (ID: {task.Id}) - Priority: {task.Priority}, Mode: {task.ExecutionMode}");

            lock (_lockObject)
            {
                _taskLookup[task.Id] = task;

                // Subscribe to task property changes
                task.PropertyChanged += OnTaskPropertyChanged;

                RunOnUIThread(() =>
                {
                    _allTasks.Add(task);
                    _pendingTasks.Add(task);
                });

                System.Diagnostics.Debug.WriteLine($"[TASK_SCHEDULER] Task scheduled. Pending: {_pendingTasks.Count}, Running: {_runningTasks.Count}, Completed: {_completedTasks.Count}");

                // Trigger queue processing within the lock to avoid race conditions
                TriggerQueueProcessing();
            }

            return task.Id;
        }

        public IMnemoTask? GetTask(Guid taskId)
        {
            return _taskLookup.TryGetValue(taskId, out var task) ? task : null;
        }

        public async Task<bool> CancelTaskAsync(Guid taskId)
        {
            var task = GetTask(taskId);
            if (task == null) return false;

            try
            {
                await task.CancelAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> PauseTaskAsync(Guid taskId)
        {
            var task = GetTask(taskId);
            if (task == null || !task.IsPausable) return false;

            try
            {
                await task.PauseAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> ResumeTaskAsync(Guid taskId)
        {
            var task = GetTask(taskId);
            if (task == null) return false;

            try
            {
                await task.ResumeAsync();
                TriggerQueueProcessing();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool RemoveTask(Guid taskId)
        {
            var task = GetTask(taskId);
            if (task == null || task.Status == Models.TaskStatus.Running) return false;

            lock (_lockObject)
            {
                _taskLookup.TryRemove(taskId, out _);
                task.PropertyChanged -= OnTaskPropertyChanged;

                RunOnUIThread(() =>
                {
                    _allTasks.Remove(task);
                    _pendingTasks.Remove(task);
                    _runningTasks.Remove(task);
                    _completedTasks.Remove(task);
                });
            }

            return true;
        }

        public void ClearCompletedTasks()
        {
            lock (_lockObject)
            {
                var completedTasks = _completedTasks.ToList();
                foreach (var task in completedTasks)
                {
                    RemoveTask(task.Id);
                }
            }
        }

        public async Task StartAsync()
        {
            if (_isRunning) 
            {
                System.Diagnostics.Debug.WriteLine("[TASK_SCHEDULER] Scheduler already running");
                return;
            }

            System.Diagnostics.Debug.WriteLine("[TASK_SCHEDULER] Starting task scheduler");
            _cancellationTokenSource = new CancellationTokenSource();
            _isRunning = true;

            // Start the processing timer
            _schedulerTimer.Change(0, 100); // Process every 100ms
            System.Diagnostics.Debug.WriteLine($"[TASK_SCHEDULER] Scheduler started - Max parallel tasks: {_maxParallelTasks}");

            await Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (!_isRunning) 
            {
                System.Diagnostics.Debug.WriteLine("[TASK_SCHEDULER] Scheduler not running");
                return;
            }

            System.Diagnostics.Debug.WriteLine("[TASK_SCHEDULER] Stopping task scheduler");
            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            // Stop the timer
            _schedulerTimer.Change(Timeout.Infinite, Timeout.Infinite);

            // Cancel all running tasks
            var runningTasks = _runningTasks.ToList();
            System.Diagnostics.Debug.WriteLine($"[TASK_SCHEDULER] Cancelling {runningTasks.Count} running tasks");
            await Task.WhenAll(runningTasks.Select(t => t.CancelAsync()));

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            System.Diagnostics.Debug.WriteLine("[TASK_SCHEDULER] Scheduler stopped");
        }

        public IEnumerable<IMnemoTask> GetSubTasks(Guid parentTaskId)
        {
            return _allTasks.Where(t => t.ParentTaskId == parentTaskId);
        }

        public IMnemoTask? GetRootTask(Guid taskId)
        {
            var task = GetTask(taskId);
            if (task == null) return null;

            while (task.ParentTaskId.HasValue)
            {
                var parent = GetTask(task.ParentTaskId.Value);
                if (parent == null) break;
                task = parent;
            }

            return task;
        }

        private void ProcessQueue(object? state)
        {
            if (!_isRunning || _cancellationTokenSource?.Token.IsCancellationRequested == true)
                return;

            try
            {
                _processingQueue = false; // Reset the flag at the start of processing
                ProcessPendingTasks();
            }
            catch (Exception ex)
            {
                _processingQueue = false; // Reset the flag even on error
                // Log error but continue processing
                System.Diagnostics.Debug.WriteLine($"[TASK_SCHEDULER] Error in task queue processing: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[TASK_SCHEDULER] Stack trace: {ex.StackTrace}");
            }
        }

        private void ProcessPendingTasks()
        {
            lock (_lockObject)
            {
                // Get tasks that can be started
                var availableTasks = _pendingTasks
                    .Where(t => t.Status == Models.TaskStatus.Pending || t.Status == Models.TaskStatus.WaitingOnQueue)
                    .OrderByDescending(t => t.Priority)
                    .ThenBy(t => t.CreatedAt)
                    .ToList();

                foreach (var task in availableTasks)
                {
                    if (CanStartTask(task))
                    {
                        // Reset WaitingOnQueue tasks back to Pending before starting
                        if (task.Status == Models.TaskStatus.WaitingOnQueue)
                        {
                            if (task is MnemoTaskBase baseTask)
                            {
                                baseTask.SetStatus(Models.TaskStatus.Pending);
                            }
                        }
                        StartTaskExecution(task);
                    }
                    else
                    {
                        // Mark tasks as waiting on queue if they can't start immediately
                        if (task.Status == Models.TaskStatus.Pending)
                        {
                            if (task is MnemoTaskBase baseTask)
                            {
                                baseTask.SetStatus(Models.TaskStatus.WaitingOnQueue);
                            }
                        }
                        // Note: WaitingOnQueue tasks that still can't start remain in that status
                    }
                }
            }
        }

        private bool CanStartTask(IMnemoTask task)
        {
            // First check if task is in a valid state to start
            if (task.Status != Models.TaskStatus.Pending && task.Status != Models.TaskStatus.WaitingOnQueue)
                return false;

            // Check if task is already in running collection (double-check)
            if (_runningTasks.Contains(task))
                return false;

            switch (task.ExecutionMode)
            {
                case TaskExecutionMode.Parallel:
                    // Check if we haven't exceeded parallel limit
                    var parallelCount = _runningTasks.Count(t => t.ExecutionMode == TaskExecutionMode.Parallel);
                    return parallelCount < _maxParallelTasks;

                case TaskExecutionMode.Exclusive:
                    // Check if no exclusive tasks are running and we can acquire the semaphore
                    var exclusiveRunning = _runningTasks.Any(t => t.ExecutionMode == TaskExecutionMode.Exclusive);
                    return !exclusiveRunning && _exclusiveTaskSemaphore.CurrentCount > 0;

                case TaskExecutionMode.UIThread:
                    // UI thread tasks can always start (they're queued on UI thread)
                    return true;

                default:
                    return false;
            }
        }

        private void StartTaskExecution(IMnemoTask task)
        {
            System.Diagnostics.Debug.WriteLine($"[TASK_SCHEDULER] Starting execution of task: '{task.Name}' (ID: {task.Id}) on {task.ExecutionMode} thread");

            // Immediately set task status to Running to prevent duplicate execution
            if (task is MnemoTaskBase baseTask)
            {
                baseTask.SetStatus(Models.TaskStatus.Running);
            }

            // Move task to running state
            RunOnUIThread(() =>
            {
                _pendingTasks.Remove(task);
                _runningTasks.Add(task);
            });

            TaskStarted?.Invoke(this, new TaskEventArgs { Task = task });

            // Start execution based on execution mode
            switch (task.ExecutionMode)
            {
                case TaskExecutionMode.UIThread:
                    System.Diagnostics.Debug.WriteLine($"[TASK_SCHEDULER] Dispatching UI thread task: '{task.Name}'");
                    _ = Dispatcher.UIThread.InvokeAsync(() => ExecuteTaskAsync(task));
                    break;

                case TaskExecutionMode.Exclusive:
                    System.Diagnostics.Debug.WriteLine($"[TASK_SCHEDULER] Starting exclusive task: '{task.Name}'");
                    _ = Task.Run(() => ExecuteExclusiveTaskAsync(task));
                    break;

                case TaskExecutionMode.Parallel:
                default:
                    System.Diagnostics.Debug.WriteLine($"[TASK_SCHEDULER] Starting parallel task: '{task.Name}'");
                    _ = Task.Run(() => ExecuteTaskAsync(task));
                    break;
            }
        }

        private async Task ExecuteExclusiveTaskAsync(IMnemoTask task)
        {
            await _exclusiveTaskSemaphore.WaitAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);
            try
            {
                await ExecuteTaskAsync(task);
            }
            finally
            {
                _exclusiveTaskSemaphore.Release();
            }
        }

        private async Task ExecuteTaskAsync(IMnemoTask task)
        {
            var startTime = DateTime.UtcNow;
            System.Diagnostics.Debug.WriteLine($"[TASK_EXECUTOR] Executing task: '{task.Name}' (ID: {task.Id})");

            try
            {
                var progress = new Progress<TaskProgress>(p =>
                {
                    TaskProgressChanged?.Invoke(this, new TaskEventArgs { Task = task, Progress = p });
                });

                var result = await task.ExecuteAsync(progress, _cancellationTokenSource?.Token ?? CancellationToken.None);

                var duration = DateTime.UtcNow - startTime;
                System.Diagnostics.Debug.WriteLine($"[TASK_EXECUTOR] Task '{task.Name}' completed in {duration.TotalMilliseconds:F0}ms - Success: {result.Success}");

                // Move task to completed
                RunOnUIThread(() =>
                {
                    _runningTasks.Remove(task);
                    _completedTasks.Add(task);
                });

                if (result.Success)
                {
                    TaskCompleted?.Invoke(this, new TaskEventArgs { Task = task });
                }
                else
                {
                    if (task.Status == Models.TaskStatus.Cancelled)
                    {
                        System.Diagnostics.Debug.WriteLine($"[TASK_EXECUTOR] Task '{task.Name}' was cancelled");
                        TaskCancelled?.Invoke(this, new TaskEventArgs { Task = task });
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[TASK_EXECUTOR] Task '{task.Name}' failed: {result.ErrorMessage}");
                        TaskFailed?.Invoke(this, new TaskEventArgs { Task = task });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                var duration = DateTime.UtcNow - startTime;
                System.Diagnostics.Debug.WriteLine($"[TASK_EXECUTOR] Task '{task.Name}' cancelled after {duration.TotalMilliseconds:F0}ms");
                
                RunOnUIThread(() =>
                {
                    _runningTasks.Remove(task);
                    _completedTasks.Add(task);
                });
                TaskCancelled?.Invoke(this, new TaskEventArgs { Task = task });
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                System.Diagnostics.Debug.WriteLine($"[TASK_EXECUTOR] Task '{task.Name}' failed after {duration.TotalMilliseconds:F0}ms: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[TASK_EXECUTOR] Exception type: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"[TASK_EXECUTOR] Stack trace: {ex.StackTrace}");
                
                RunOnUIThread(() =>
                {
                    _runningTasks.Remove(task);
                    _completedTasks.Add(task);
                });
                TaskFailed?.Invoke(this, new TaskEventArgs { Task = task });
            }
        }

        private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is IMnemoTask task && e.PropertyName == nameof(IMnemoTask.Status))
            {
                // Handle status changes that might affect scheduling
                if (task.Status == Models.TaskStatus.Pending)
                {
                    TriggerQueueProcessing();
                }
            }
        }

        private void TriggerQueueProcessing()
        {
            if (_isRunning && !_processingQueue)
            {
                _processingQueue = true;
                _schedulerTimer.Change(0, 100);
            }
        }

        private static void RunOnUIThread(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.UIThread.Post(action);
            }
        }

        public void Dispose()
        {
            _schedulerTimer?.Dispose();
            _cancellationTokenSource?.Dispose();
            _exclusiveTaskSemaphore?.Dispose();
        }
    }
}
