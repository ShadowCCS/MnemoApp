using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Threading;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.UI.Components.Overlays
{
    public partial class LoadingOverlay : UserControl, INotifyPropertyChanged
    {
        private readonly ITaskScheduler? _taskScheduler;
        private IMnemoTask? _task;
        private string? _title;
        private string? _description;
        private double _progress;
        private string? _progressText;
        private double _progressWidth = 0;
        

        public LoadingOverlay()
        {
            // Initialize SubTasks first to prevent null reference
            SubTasks = new ObservableCollection<SubTaskViewModel>();
            
            InitializeComponent();
            DataContext = this;
        }
        
        public LoadingOverlay(ITaskScheduler taskScheduler) : this()
        {
            _taskScheduler = taskScheduler;
        }

        public string? Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(); } }
        }

        public string? Description
        {
            get => _description;
            set { if (_description != value) { _description = value; OnPropertyChanged(); } }
        }

        public double Progress
        {
            get => _progress;
            set 
            { 
                if (Math.Abs(_progress - value) > 0.001) 
                { 
                    _progress = value; 
                    OnPropertyChanged();
                    UpdateProgressWidth();
                } 
            }
        }

        public string? ProgressText
        {
            get => _progressText;
            set { if (_progressText != value) { _progressText = value; OnPropertyChanged(); } }
        }

        public double ProgressWidth
        {
            get => _progressWidth;
            private set { if (Math.Abs(_progressWidth - value) > 0.001) { _progressWidth = value; OnPropertyChanged(); } }
        }

        

        

        public ObservableCollection<SubTaskViewModel> SubTasks { get; }

        public bool HasSubTasks => SubTasks.Count > 0;

        
        

        public event Action? MinimizeRequested;
        #pragma warning disable CS0067 // Event is never used
        public event Action? CancelRequested;
        #pragma warning restore CS0067

        public void SetTask(IMnemoTask task)
        {
            if (_task != null)
            {
                _task.PropertyChanged -= OnTaskPropertyChanged;
            }

            _task = task;
            
            if (_task != null)
            {
                _task.PropertyChanged += OnTaskPropertyChanged;
                UpdateFromTask();
                UpdateSubTasks();
            }
        }

        private void UpdateFromTask()
        {
            if (_task == null) return;

            Title = _task.Name;
            Description = _task.Description;
            Progress = _task.Progress;
            ProgressText = _task.ProgressText;
            
            
        }

        private void UpdateSubTasks()
        {
            if (_task == null || _taskScheduler == null) return;

            var subTasks = _taskScheduler.GetSubTasks(_task.Id);
            
            Dispatcher.UIThread.Post(() =>
            {
                SubTasks.Clear();
                foreach (var subTask in subTasks)
                {
                    SubTasks.Add(new SubTaskViewModel(subTask));
                }
                OnPropertyChanged(nameof(HasSubTasks));
            });
        }

        private void UpdateProgressWidth()
        {
            // Assuming container width of ~400px, adjust the progress bar width
            ProgressWidth = Progress * 400;
        }

        private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(IMnemoTask.Progress):
                        Progress = _task?.Progress ?? 0;
                        break;
                    case nameof(IMnemoTask.ProgressText):
                        ProgressText = _task?.ProgressText;
                        break;
                    case nameof(IMnemoTask.Status):
                        UpdateFromTask();
                // Auto-close overlay when task finishes (Completed/Failed/Cancelled)
                        if (_task != null && (_task.Status == Mnemo.Core.Services.TaskStatus.Completed || _task.Status == Mnemo.Core.Services.TaskStatus.Failed || _task.Status == Mnemo.Core.Services.TaskStatus.Cancelled))
                        {
                            // Slight delay to allow UI to render final state
                    Dispatcher.UIThread.Post(() => MinimizeRequested?.Invoke());
                        }
                        break;
                    case nameof(IMnemoTask.SubTaskIds):
                        UpdateSubTasks();
                        break;
                }
            });
        }

        

        

        public new event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class SubTaskViewModel : INotifyPropertyChanged
    {
        private readonly IMnemoTask _task;

        public SubTaskViewModel(IMnemoTask task)
        {
            _task = task;
            _task.PropertyChanged += OnTaskPropertyChanged;
        }

        public string Name => _task.Name;

        public string StatusText
        {
            get
            {
                return _task.Status switch
                {
                    Mnemo.Core.Services.TaskStatus.Pending => "Waiting...",
                    Mnemo.Core.Services.TaskStatus.Running => $"{_task.Progress:P0}",
                    Mnemo.Core.Services.TaskStatus.Completed => "✓ Done",
                    Mnemo.Core.Services.TaskStatus.Failed => "✗ Failed",
                    Mnemo.Core.Services.TaskStatus.Cancelled => "✗ Cancelled",
                    Mnemo.Core.Services.TaskStatus.Paused => "⏸ Paused",
                    _ => ""
                };
            }
        }

        private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IMnemoTask.Status) || 
                e.PropertyName == nameof(IMnemoTask.Progress))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

