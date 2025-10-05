using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace MnemoApp.Core.Tasks.Models
{
    public enum TaskStatus
    {
        Pending,
        WaitingOnQueue,
        Running,
        Completed,
        Failed,
        Cancelled,
        Paused
    }

    public enum TaskPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    public enum TaskExecutionMode
    {
        /// <summary>
        /// Task can run in parallel with other Parallel tasks
        /// </summary>
        Parallel,
        /// <summary>
        /// Task must run exclusively (e.g., AI inference)
        /// </summary>
        Exclusive,
        /// <summary>
        /// Task runs on UI thread
        /// </summary>
        UIThread
    }

    /// <summary>
    /// Base interface for all tasks in the system
    /// </summary>
    public interface IMnemoTask : INotifyPropertyChanged
    {
        Guid Id { get; }
        string Name { get; }
        string? Description { get; }
        TaskStatus Status { get; }
        TaskPriority Priority { get; }
        TaskExecutionMode ExecutionMode { get; }
        double Progress { get; }
        string? ProgressText { get; }
        bool UsingAI { get; }
        DateTime CreatedAt { get; }
        DateTime? StartedAt { get; }
        DateTime? CompletedAt { get; }
        TimeSpan? EstimatedDuration { get; }
        string? ErrorMessage { get; }
        Guid? ParentTaskId { get; }
        IReadOnlyList<Guid> SubTaskIds { get; }
        bool IsCancellable { get; }
        bool IsPausable { get; }
        CancellationToken CancellationToken { get; }
        TaskResult? Result { get; }

        /// <summary>
        /// Execute the task
        /// </summary>
        Task<TaskResult> ExecuteAsync(IProgress<TaskProgress>? progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Cancel the task if possible
        /// </summary>
        Task CancelAsync();

        /// <summary>
        /// Pause the task if possible
        /// </summary>
        Task PauseAsync();

        /// <summary>
        /// Resume the task if paused
        /// </summary>
        Task ResumeAsync();
    }

    /// <summary>
    /// Progress information for a task
    /// </summary>
    public record TaskProgress(double ProgressValue, string? ProgressText = null, string? CurrentOperation = null);

    /// <summary>
    /// Result of task execution
    /// </summary>
    public record TaskResult(bool Success, object? Data = null, string? ErrorMessage = null);

    /// <summary>
    /// Base implementation of IMnemoTask
    /// </summary>
    public abstract class MnemoTaskBase : IMnemoTask
    {
        private readonly List<Guid> _subTaskIds = new();
        private TaskStatus _status = TaskStatus.Pending;
        private double _progress = 0.0;
        private string? _progressText;
        private DateTime? _startedAt;
        private DateTime? _completedAt;
        private string? _errorMessage;
        private CancellationTokenSource? _cancellationTokenSource;
        private TaskResult? _result;

        protected MnemoTaskBase(string name, string? description = null, TaskPriority priority = TaskPriority.Normal, TaskExecutionMode executionMode = TaskExecutionMode.Parallel, bool usingAI = false, Guid? parentTaskId = null)
        {
            Id = Guid.NewGuid();
            Name = name;
            Description = description;
            Priority = priority;
            ExecutionMode = executionMode;
            UsingAI = usingAI;
            ParentTaskId = parentTaskId;
            CreatedAt = DateTime.UtcNow;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public Guid Id { get; }
        public string Name { get; }
        public string? Description { get; }
        public TaskPriority Priority { get; }
        public TaskExecutionMode ExecutionMode { get; }
        public bool UsingAI { get; }
        public DateTime CreatedAt { get; }
        public Guid? ParentTaskId { get; }
        public virtual bool IsCancellable => true;
        public virtual bool IsPausable => false;
        public virtual TimeSpan? EstimatedDuration => null;

        public TaskStatus Status
        {
            get => _status;
            protected set { if (_status != value) { _status = value; OnPropertyChanged(); } }
        }

        public double Progress
        {
            get => _progress;
            protected set { if (Math.Abs(_progress - value) > 0.001) { _progress = value; OnPropertyChanged(); } }
        }

        public string? ProgressText
        {
            get => _progressText;
            protected set { if (_progressText != value) { _progressText = value; OnPropertyChanged(); } }
        }

        public DateTime? StartedAt
        {
            get => _startedAt;
            protected set { if (_startedAt != value) { _startedAt = value; OnPropertyChanged(); } }
        }

        public DateTime? CompletedAt
        {
            get => _completedAt;
            protected set { if (_completedAt != value) { _completedAt = value; OnPropertyChanged(); } }
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            protected set { if (_errorMessage != value) { _errorMessage = value; OnPropertyChanged(); } }
        }

        public IReadOnlyList<Guid> SubTaskIds => _subTaskIds.AsReadOnly();

        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;

        public TaskResult? Result
        {
            get => _result;
            private set { if (_result != value) { _result = value; OnPropertyChanged(); } }
        }

        public async Task<TaskResult> ExecuteAsync(IProgress<TaskProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            // Allow execution if:
            // - Pending (normal path)
            // - Paused (resume path)
            // - Running but not actually started yet (scheduler pre-set status to prevent duplicates)
            var isSchedulerPreStart = Status == TaskStatus.Running && StartedAt == null;
            if (Status != TaskStatus.Pending && Status != TaskStatus.Paused && !isSchedulerPreStart)
            {
                System.Diagnostics.Debug.WriteLine($"[TASK_BASE] Task '{Name}' is not in valid state to execute. Current status: {Status}");
                return new TaskResult(false, ErrorMessage: "Task is not in a valid state to execute");
            }

            System.Diagnostics.Debug.WriteLine($"[TASK_BASE] Starting execution of task: '{Name}' (ID: {Id})");
            Status = TaskStatus.Running;
            StartedAt = DateTime.UtcNow;
            Progress = 0.0;
            ErrorMessage = null;

            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, cancellationToken);
                
                var progressReporter = new Progress<TaskProgress>(p =>
                {
                    Progress = p.ProgressValue;
                    ProgressText = p.ProgressText;
                    progress?.Report(p);
                });

                var result = await ExecuteTaskAsync(progressReporter, linkedCts.Token);
                Result = result;

                if (result.Success)
                {
                    Status = TaskStatus.Completed;
                    Progress = 1.0;
                    CompletedAt = DateTime.UtcNow;
                    System.Diagnostics.Debug.WriteLine($"[TASK_BASE] Task '{Name}' completed successfully");
                }
                else
                {
                    Status = TaskStatus.Failed;
                    ErrorMessage = result.ErrorMessage;
                    CompletedAt = DateTime.UtcNow;
                    System.Diagnostics.Debug.WriteLine($"[TASK_BASE] Task '{Name}' failed: {result.ErrorMessage}");
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                Status = TaskStatus.Cancelled;
                CompletedAt = DateTime.UtcNow;
                Result = new TaskResult(false, ErrorMessage: "Task was cancelled");
                System.Diagnostics.Debug.WriteLine($"[TASK_BASE] Task '{Name}' was cancelled");
                return Result;
            }
            catch (Exception ex)
            {
                Status = TaskStatus.Failed;
                ErrorMessage = ex.Message;
                CompletedAt = DateTime.UtcNow;
                Result = new TaskResult(false, ErrorMessage: ex.Message);
                System.Diagnostics.Debug.WriteLine($"[TASK_BASE] Task '{Name}' threw exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[TASK_BASE] Exception type: {ex.GetType().Name}");
                return Result;
            }
        }

        public virtual async Task CancelAsync()
        {
            if (IsCancellable && Status == TaskStatus.Running)
            {
                _cancellationTokenSource?.Cancel();
                await OnCancelAsync();
            }
        }

        public virtual async Task PauseAsync()
        {
            if (IsPausable && Status == TaskStatus.Running)
            {
                Status = TaskStatus.Paused;
                await OnPauseAsync();
            }
        }

        public virtual async Task ResumeAsync()
        {
            if (Status == TaskStatus.Paused)
            {
                Status = TaskStatus.Pending;
                await OnResumeAsync();
            }
        }

        /// <summary>
        /// Internal method for scheduler to set task status
        /// </summary>
        internal void SetStatus(TaskStatus status)
        {
            Status = status;
        }

        protected void AddSubTask(Guid subTaskId)
        {
            _subTaskIds.Add(subTaskId);
            OnPropertyChanged(nameof(SubTaskIds));
        }

        protected void RemoveSubTask(Guid subTaskId)
        {
            _subTaskIds.Remove(subTaskId);
            OnPropertyChanged(nameof(SubTaskIds));
        }

        /// <summary>
        /// Implement the actual task logic
        /// </summary>
        protected abstract Task<TaskResult> ExecuteTaskAsync(IProgress<TaskProgress> progress, CancellationToken cancellationToken);

        /// <summary>
        /// Called when task is cancelled
        /// </summary>
        protected virtual Task OnCancelAsync() => Task.CompletedTask;

        /// <summary>
        /// Called when task is paused
        /// </summary>
        protected virtual Task OnPauseAsync() => Task.CompletedTask;

        /// <summary>
        /// Called when task is resumed
        /// </summary>
        protected virtual Task OnResumeAsync() => Task.CompletedTask;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
