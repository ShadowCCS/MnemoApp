using System;
using System.Collections.Concurrent;
using System.Linq;
using MnemoApp.Core.Overlays;
using MnemoApp.Core.Tasks.Models;
using MnemoApp.Core.Tasks.Services;
using MnemoApp.UI.Components.Overlays;

namespace MnemoApp.Core.MnemoAPI
{
    /// <summary>
    /// API for managing loading overlays tied to tasks
    /// </summary>
    public class LoadingOverlayApi
    {
        private readonly IOverlayService _overlayService;
        private readonly ITaskSchedulerService? _taskScheduler;
        private readonly ConcurrentDictionary<Guid, Guid> _taskToOverlayMap = new();

        public LoadingOverlayApi(IOverlayService overlayService, ITaskSchedulerService? taskScheduler = null)
        {
            _overlayService = overlayService ?? throw new ArgumentNullException(nameof(overlayService));
            _taskScheduler = taskScheduler;
        }

        /// <summary>
        /// Show a loading overlay for a specific task
        /// </summary>
        public Guid showForTask(Guid taskId, OverlayOptions? options = null)
        {
            if (_taskScheduler == null)
                throw new InvalidOperationException("Task scheduler service is not available");

            var task = _taskScheduler.GetTask(taskId);
            if (task == null)
                throw new ArgumentException($"Task with ID {taskId} not found", nameof(taskId));

            return ShowLoadingOverlay(task, options);
        }

        /// <summary>
        /// Show a loading overlay with custom text and progress
        /// </summary>
        public Guid show(string title, string? description = null, double initialProgress = 0.0, string? progressText = null, OverlayOptions? options = null)
        {
            var loadingOverlay = new LoadingOverlay
            {
                Title = title,
                Description = description,
                Progress = initialProgress,
                ProgressText = progressText
            };

            var overlayOptions = options ?? new OverlayOptions
            {
                ShowBackdrop = true,
                BackdropOpacity = 0.6,
                CloseOnOutsideClick = true
            };

            var overlayId = _overlayService.CreateOverlay(loadingOverlay, overlayOptions, "loading-overlay");

            loadingOverlay.MinimizeRequested += () => _overlayService.CloseOverlay(overlayId);
            loadingOverlay.CancelRequested += () => _overlayService.CloseOverlay(overlayId);

            return overlayId;
        }

        /// <summary>
        /// Update a loading overlay's progress and text
        /// </summary>
        public bool update(Guid overlayId, double? progress = null, string? progressText = null, string? title = null, string? description = null)
        {
            var overlay = GetLoadingOverlay(overlayId);
            if (overlay == null) return false;

            if (progress.HasValue) overlay.Progress = progress.Value;
            if (progressText != null) overlay.ProgressText = progressText;
            if (title != null) overlay.Title = title;
            if (description != null) overlay.Description = description;

            return true;
        }

        /// <summary>
        /// Close a loading overlay
        /// </summary>
        public bool close(Guid overlayId)
        {
            // Remove from task mapping if it exists
            var taskId = GetTaskIdForOverlay(overlayId);
            if (taskId.HasValue)
            {
                _taskToOverlayMap.TryRemove(taskId.Value, out _);
            }

            return _overlayService.CloseOverlay(overlayId);
        }

        /// <summary>
        /// Check if a loading overlay is currently open for a task
        /// </summary>
        public bool isOpenForTask(Guid taskId)
        {
            if (!_taskToOverlayMap.TryGetValue(taskId, out var overlayId))
                return false;

            // Check if overlay still exists
            return _overlayService.Overlays.Any(o => o.Id == overlayId);
        }

        /// <summary>
        /// Get the overlay ID for a specific task
        /// </summary>
        public Guid? getOverlayIdForTask(Guid taskId)
        {
            return _taskToOverlayMap.TryGetValue(taskId, out var overlayId) ? overlayId : null;
        }

        private Guid ShowLoadingOverlay(IMnemoTask task, OverlayOptions? options = null)
        {
            // Close existing overlay for this task if it exists
            if (_taskToOverlayMap.TryGetValue(task.Id, out var existingOverlayId))
            {
                _overlayService.CloseOverlay(existingOverlayId);
            }

            var loadingOverlay = new LoadingOverlay();
            loadingOverlay.SetTask(task);

            var overlayOptions = options ?? new OverlayOptions
            {
                ShowBackdrop = true,
                BackdropOpacity = 0.6,
                CloseOnOutsideClick = true
            };

            var overlayId = _overlayService.CreateOverlay(loadingOverlay, overlayOptions, $"task-loading-{task.Id}");

            _taskToOverlayMap[task.Id] = overlayId;

            loadingOverlay.MinimizeRequested += () => 
            {
                // Ensure mapping is cleared when minimized (auto-close)
                _taskToOverlayMap.TryRemove(task.Id, out _);
                _overlayService.CloseOverlay(overlayId);
            };
            loadingOverlay.CancelRequested += () => 
            {
                _taskToOverlayMap.TryRemove(task.Id, out _);
                _overlayService.CloseOverlay(overlayId);
            };

            return overlayId;
        }

        private LoadingOverlay? GetLoadingOverlay(Guid overlayId)
        {
            var overlayInstance = _overlayService.Overlays.FirstOrDefault(o => o.Id == overlayId);
            return overlayInstance?.Content as LoadingOverlay;
        }

        private Guid? GetTaskIdForOverlay(Guid overlayId)
        {
            return _taskToOverlayMap.FirstOrDefault(kvp => kvp.Value == overlayId).Key;
        }
    }
}
