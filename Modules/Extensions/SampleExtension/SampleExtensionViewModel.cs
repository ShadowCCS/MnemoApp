using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using MnemoApp.Core.Common;
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Core.Extensions;
using MnemoApp.Core.Tasks.Models;
using MnemoApp.Core.Services;
using MnemoApp.Data.Runtime;
using MnemoApp.Extensions.SampleExtension.Tasks;

namespace MnemoApp.Extensions.SampleExtension
{
    /// <summary>
    /// View model for the Sample Extension demonstrating various extension capabilities
    /// </summary>
    public class SampleExtensionViewModel : ViewModelBase
    {
        private IExtensionContext? _context;
        private string _inputText = "Hello from Sample Extension!";
        private string _statusMessage = "Ready";
        private bool _isProcessing;
        private ObservableCollection<TaskInfo> _recentTasks = new();

        public SampleExtensionViewModel()
        {
            InitializeCommands();
            LoadInitialData();
        }

        /// <summary>
        /// Set the extension context (called by the extension system)
        /// </summary>
        public void SetExtensionContext(IExtensionContext context)
        {
            _context = context;
        }

        #region Properties

        public string InputText
        {
            get => _inputText;
            set => SetProperty(ref _inputText, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set => SetProperty(ref _isProcessing, value);
        }

        public ObservableCollection<TaskInfo> RecentTasks
        {
            get => _recentTasks;
            set => SetProperty(ref _recentTasks, value);
        }

        public int LoadCount => _context?.API.data.GetProperty<int>($"{_context?.StoragePrefix}loadCount", 0) ?? 0;

        public string ExtensionName => _context?.Metadata.Manifest.DisplayName ?? _context?.Metadata.Manifest.Name ?? "Unknown";

        public string ExtensionVersion => _context?.Metadata.Manifest.Version ?? "Unknown";

        #endregion

        #region Commands

        public ICommand ScheduleTaskCommand { get; private set; } = null!;
        public ICommand ShowToastCommand { get; private set; } = null!;
        public ICommand ShowOverlayCommand { get; private set; } = null!;
        public ICommand TestAICommand { get; private set; } = null!;
        public ICommand TestStorageCommand { get; private set; } = null!;
        public ICommand ClearTasksCommand { get; private set; } = null!;
        public ICommand RefreshTasksCommand { get; private set; } = null!;

        #endregion

        private void InitializeCommands()
        {
            ScheduleTaskCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ScheduleTaskAsync);
            ShowToastCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => ShowToast());
            ShowOverlayCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => ShowOverlay());
            TestAICommand = new CommunityToolkit.Mvvm.Input.RelayCommand(async () => await TestAIAsync());
            TestStorageCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => TestStorage());
            ClearTasksCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => ClearTasks());
            RefreshTasksCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => RefreshTasks());
        }

        private void LoadInitialData()
        {
            StatusMessage = $"Extension loaded {LoadCount} times";
            RefreshTasks();
        }

        private void ScheduleTaskAsync()
        {
            if (_context == null)
            {
                StatusMessage = "Extension context not initialized";
                return;
            }

            if (string.IsNullOrWhiteSpace(InputText))
            {
                _context.API.ui.toast.show("Error", "Please enter some text to process", ToastType.Error);
                return;
            }

            try
            {
                IsProcessing = true;
                StatusMessage = "Scheduling task...";

                var task = new SampleTask(
                    _context.Services.GetRequiredService<IRuntimeStorage>(),
                    InputText
                );

                var taskId = _context.API.tasks.scheduleTask(task);
                
                // Show progress toast
                _context.API.ui.toast.showForTask(taskId, showProgress: true);
                
                StatusMessage = $"Task scheduled with ID: {taskId}";
                
                // Refresh task list
                RefreshTasks();
            }
            catch (Exception ex)
            {
                _context.Logger.LogError("Failed to schedule task", ex);
                _context.API.ui.toast.show("Error", $"Failed to schedule task: {ex.Message}", ToastType.Error);
                StatusMessage = "Error scheduling task";
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void ShowToast()
        {
            if (_context == null)
            {
                StatusMessage = "Extension context not initialized";
                return;
            }

            var toastId = _context.API.ui.toast.show(
                "Sample Toast",
                $"This is a toast from {ExtensionName} v{ExtensionVersion}",
                ToastType.Info,
                TimeSpan.FromSeconds(3)
            );
            
            StatusMessage = $"Toast shown with ID: {toastId}";
        }

        private void ShowOverlay()
        {
            if (_context == null)
            {
                StatusMessage = "Extension context not initialized";
                return;
            }

            var overlay = new SampleOverlayControl(_context);
            _context.API.ui.overlay.Show<object>(overlay);
            
            StatusMessage = "Overlay opened";
        }

        private async Task TestAIAsync()
        {
            if (_context == null)
            {
                StatusMessage = "Extension context not initialized";
                return;
            }

            try
            {
                IsProcessing = true;
                StatusMessage = "Testing AI capabilities...";

                // Get available models
                var models = await _context.API.ai.GetAllModelsAsync();
                
                if (models.Any())
                {
                    var selectedModel = models.FirstOrDefault();
                    StatusMessage = $"Found {models.Count} AI models. Testing with: {selectedModel?.Manifest.Name}";
                    
                    // Create a simple request
                    var request = _context.API.ai.CreateRequest(
                        selectedModel?.Manifest.Name ?? "default",
                        "Write a short greeting message for a sample extension."
                    );
                    
                    var response = await _context.API.ai.InferAsync(request);
                    
                    _context.API.ui.toast.show("AI Response", response.Response, ToastType.Success);
                }
                else
                {
                    StatusMessage = "No AI models available";
                    _context.API.ui.toast.show("AI Test", "No AI models are currently available", ToastType.Warning);
                }
            }
            catch (Exception ex)
            {
                _context.Logger.LogError("AI test failed", ex);
                _context.API.ui.toast.show("AI Error", $"AI test failed: {ex.Message}", ToastType.Error);
                StatusMessage = "AI test failed";
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void TestStorage()
        {
            if (_context == null)
            {
                StatusMessage = "Extension context not initialized";
                return;
            }

            try
            {
                var testKey = $"{_context.StoragePrefix}test_{DateTime.Now:yyyyMMdd_HHmmss}";
                var testValue = $"Test data stored at {DateTime.Now:HH:mm:ss}";

                _context.API.data.SetProperty(testKey, testValue);
                
                var retrievedValue = _context.API.data.GetProperty<string>(testKey);
                
                if (retrievedValue == testValue)
                {
                    StatusMessage = "Storage test successful";
                    _context.API.ui.toast.show("Storage Test", "Data stored and retrieved successfully!", ToastType.Success);
                }
                else
                {
                    StatusMessage = "Storage test failed - data mismatch";
                    _context.API.ui.toast.show("Storage Error", "Data mismatch in storage test", ToastType.Error);
                }
            }
            catch (Exception ex)
            {
                _context.Logger.LogError("Storage test failed", ex);
                _context.API.ui.toast.show("Storage Error", $"Storage test failed: {ex.Message}", ToastType.Error);
                StatusMessage = "Storage test failed";
            }
        }

        private void ClearTasks()
        {
            RecentTasks.Clear();
            StatusMessage = "Task list cleared";
        }

        private void RefreshTasks()
        {
            if (_context == null)
            {
                StatusMessage = "Extension context not initialized";
                return;
            }

            RecentTasks.Clear();

            // Get recent tasks from the task system
            var allTasks = _context.API.tasks.allTasks;
            var recentTaskInfos = allTasks
                .Where(t => t.Name.Contains("Sample"))
                .OrderByDescending(t => t.CreatedAt)
                .Take(10)
                .Select(t => new TaskInfo
                {
                    Id = t.Id,
                    Name = t.Name,
                    Status = t.Status.ToString(),
                    CreatedAt = t.CreatedAt,
                    Progress = t.Progress
                });

            foreach (var taskInfo in recentTaskInfos)
            {
                RecentTasks.Add(taskInfo);
            }

            StatusMessage = $"Found {RecentTasks.Count} recent sample tasks";
        }
    }

    /// <summary>
    /// Simple data class for displaying task information
    /// </summary>
    public class TaskInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public double Progress { get; set; }
    }
}
