using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using MnemoApp.Core.Tasks.Models;
using MnemoApp.Core.Tasks.Services;
using MnemoApp.Core.Tasks.Examples;
using MnemoApp.Core.Tasks;
using MnemoApp.Core.AI.Services;
using MnemoApp.Core.AI.Models;
using MnemoApp.Data.Runtime;

namespace MnemoApp.Core.MnemoAPI
{
    /// <summary>
    /// Task management API for scheduling and managing tasks
    /// </summary>
    public class TaskApi
    {
        private readonly ITaskSchedulerService _taskScheduler;
        private readonly IAIService? _aiService;
        private readonly IModelSelectionService? _modelSelectionService;
        private readonly IRuntimeStorage? _storage;

        public TaskApi(ITaskSchedulerService taskScheduler, IAIService? aiService = null, IModelSelectionService? modelSelectionService = null, IRuntimeStorage? storage = null)
        {
            _taskScheduler = taskScheduler ?? throw new ArgumentNullException(nameof(taskScheduler));
            _aiService = aiService;
            _modelSelectionService = modelSelectionService;
            _storage = storage;
        }

        /// <summary>
        /// All tasks in the system
        /// </summary>
        public ReadOnlyObservableCollection<IMnemoTask> allTasks => _taskScheduler.AllTasks;

        /// <summary>
        /// Currently running tasks
        /// </summary>
        public ReadOnlyObservableCollection<IMnemoTask> runningTasks => _taskScheduler.RunningTasks;

        /// <summary>
        /// Pending tasks waiting to be executed
        /// </summary>
        public ReadOnlyObservableCollection<IMnemoTask> pendingTasks => _taskScheduler.PendingTasks;

        /// <summary>
        /// Completed tasks
        /// </summary>
        public ReadOnlyObservableCollection<IMnemoTask> completedTasks => _taskScheduler.CompletedTasks;

        /// <summary>
        /// Whether the scheduler is running
        /// </summary>
        public bool isRunning => _taskScheduler.IsRunning;

        /// <summary>
        /// Maximum parallel tasks
        /// </summary>
        public int maxParallelTasks
        {
            get => _taskScheduler.MaxParallelTasks;
            set => _taskScheduler.MaxParallelTasks = value;
        }

        /// <summary>
        /// Schedule a custom task
        /// </summary>
        public Guid scheduleTask(IMnemoTask task)
        {
            return _taskScheduler.ScheduleTask(task);
        }

        /// <summary>
        /// Get a task by ID
        /// </summary>
        public IMnemoTask? getTask(Guid taskId)
        {
            return _taskScheduler.GetTask(taskId);
        }

        /// <summary>
        /// Cancel a task
        /// </summary>
        public async Task<bool> cancelTask(Guid taskId)
        {
            return await _taskScheduler.CancelTaskAsync(taskId);
        }

        /// <summary>
        /// Pause a task
        /// </summary>
        public async Task<bool> pauseTask(Guid taskId)
        {
            return await _taskScheduler.PauseTaskAsync(taskId);
        }

        /// <summary>
        /// Resume a task
        /// </summary>
        public async Task<bool> resumeTask(Guid taskId)
        {
            return await _taskScheduler.ResumeTaskAsync(taskId);
        }

        /// <summary>
        /// Remove a completed task
        /// </summary>
        public bool removeTask(Guid taskId)
        {
            return _taskScheduler.RemoveTask(taskId);
        }

        /// <summary>
        /// Clear all completed tasks
        /// </summary>
        public void clearCompletedTasks()
        {
            _taskScheduler.ClearCompletedTasks();
        }

        /// <summary>
        /// Get sub-tasks for a parent task
        /// </summary>
        public System.Collections.Generic.IEnumerable<IMnemoTask> getSubTasks(Guid parentTaskId)
        {
            return _taskScheduler.GetSubTasks(parentTaskId);
        }

        /// <summary>
        /// Get the root task for a given task
        /// </summary>
        public IMnemoTask? getRootTask(Guid taskId)
        {
            return _taskScheduler.GetRootTask(taskId);
        }

        // Convenience methods for common task types

        /// <summary>
        /// Schedule an AI generation task
        /// </summary>
        public Guid scheduleAIGeneration(string prompt, string name, string? description = null, string? modelName = null, int maxTokens = 1000)
        {
            if (_aiService == null)
                throw new InvalidOperationException("AI service is not available");

            // Map placeholder to actual selected/available model here to avoid passing "default" downstream
            string effectiveModel = modelName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(effectiveModel) || effectiveModel.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                effectiveModel = _modelSelectionService?.SelectedModel ?? effectiveModel;
                
                if (string.IsNullOrWhiteSpace(effectiveModel))
                {
                    var names = _aiService.GetAllNamesAsync().GetAwaiter().GetResult();
                    if (names.Count > 0)
                    {
                        effectiveModel = names[0];
                    }
                }
                if (string.IsNullOrWhiteSpace(effectiveModel))
                {
                    throw new InvalidOperationException("No AI model selected or available");
                }
            }

            var request = new AIInferenceRequest
            {
                ModelName = effectiveModel,
                Prompt = prompt,
                MaxTokens = maxTokens
            };

            var task = new AIGenerationTask(_aiService, request, name, description, _modelSelectionService);
            return _taskScheduler.ScheduleTask(task);
        }

        /// <summary>
        /// Schedule a file parsing task
        /// </summary>
        public Guid scheduleParseAttachments(string[] filePaths)
        {
            var task = new ParseAttachmentsTask(filePaths);
            return _taskScheduler.ScheduleTask(task);
        }

        /// <summary>
        /// Schedule a learning path generation task
        /// </summary>
        public Guid scheduleGeneratePath(string pathTopic, int unitCount)
        {
            if (_aiService == null)
                throw new InvalidOperationException("AI service is not available");

            var task = new GeneratePathTask(_aiService, pathTopic, unitCount, _modelSelectionService);
            return _taskScheduler.ScheduleTask(task);
        }

        /// <summary>
        /// Schedule a learning path creation task from notes (core feature)
        /// </summary>
        public Guid scheduleCreatePath(string notes)
        {
            if (_aiService == null)
                throw new InvalidOperationException("AI service is not available");
            
            if (_storage == null)
                throw new InvalidOperationException("Storage service is not available");

            var task = new CreateLearningPathTask(_aiService, _storage, notes, _modelSelectionService);
            return _taskScheduler.ScheduleTask(task);
        }

        /// <summary>
        /// Start the task scheduler
        /// </summary>
        public async Task start()
        {
            await _taskScheduler.StartAsync();
        }

        /// <summary>
        /// Stop the task scheduler
        /// </summary>
        public async Task stop()
        {
            await _taskScheduler.StopAsync();
        }

        // Event subscriptions
        public void onTaskStarted(Action<IMnemoTask> handler)
        {
            _taskScheduler.TaskStarted += (sender, args) => handler(args.Task);
        }

        public void onTaskCompleted(Action<IMnemoTask> handler)
        {
            _taskScheduler.TaskCompleted += (sender, args) => handler(args.Task);
        }

        public void onTaskFailed(Action<IMnemoTask> handler)
        {
            _taskScheduler.TaskFailed += (sender, args) => handler(args.Task);
        }

        public void onTaskCancelled(Action<IMnemoTask> handler)
        {
            _taskScheduler.TaskCancelled += (sender, args) => handler(args.Task);
        }

        public void onTaskProgressChanged(Action<IMnemoTask, TaskProgress?> handler)
        {
            _taskScheduler.TaskProgressChanged += (sender, args) => handler(args.Task, args.Progress);
        }
    }
}
