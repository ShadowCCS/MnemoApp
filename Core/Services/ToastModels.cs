using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MnemoApp.Core.Services
{
    public enum ToastType
    {
        Info,
        Error,
        Success,
        Warning,
        Process
    }

    public sealed class ToastNotification : INotifyPropertyChanged
    {
        public Guid Id { get; } = Guid.NewGuid();

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(); } }
        }

        private string? _message;
        public string? Message
        {
            get => _message;
            set { if (_message != value) { _message = value; OnPropertyChanged(); } }
        }

        private ToastType _type;
        public ToastType Type
        {
            get => _type;
            set { if (_type != value) { _type = value; OnPropertyChanged(); OnPropertyChanged(nameof(IconPath)); } }
        }

        public bool Dismissable { get; set; } = true;

        public TimeSpan? Duration { get; set; } = TimeSpan.FromSeconds(4);

        public bool IsIndefinite => Duration == null || Duration <= TimeSpan.Zero;

        public DateTime CreatedAt { get; } = DateTime.UtcNow;

        public bool IsStatus { get; set; }

        private Guid? _taskId;
        public Guid? TaskId
        {
            get => _taskId;
            set { if (_taskId != value) { _taskId = value; OnPropertyChanged(); } }
        }

        private double? _progress;
        public double? Progress
        {
            get => _progress;
            set { if (_progress != value) { _progress = value; OnPropertyChanged(); } }
        }

        private string? _progressText;
        public string? ProgressText
        {
            get => _progressText;
            set { if (_progressText != value) { _progressText = value; OnPropertyChanged(); } }
        }

        public string IconPath
        {
            get
            {
                var typeString = Type.ToString().ToLowerInvariant();
                return $"avares://MnemoApp/UI/Icons/Toast/system_{typeString}.svg";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public interface IToastService
    {
        System.Collections.ObjectModel.ReadOnlyObservableCollection<ToastNotification> PassiveToasts { get; }
        System.Collections.ObjectModel.ReadOnlyObservableCollection<ToastNotification> StatusToasts { get; }
        int MaxPassive { get; set; }

        Guid Show(string title, string? message = null, ToastType type = ToastType.Info, TimeSpan? duration = null, bool dismissable = true);
        Guid ShowStatus(string title, string? message = null, ToastType type = ToastType.Process, bool dismissable = true, double? initialProgress = null, string? progressText = null);
        bool TryUpdateStatus(Guid id, double? progress = null, string? progressText = null, string? title = null, string? message = null, ToastType? type = null);
        bool CompleteStatus(Guid id);
        bool Remove(Guid id);
        bool RemovePassive(Guid id);
        bool RemoveStatus(Guid id);
        void Clear();

        // Associate a status toast with a task for deep-link actions (e.g., open overlay)
        bool AttachTask(System.Guid toastId, System.Guid taskId);
    }
}


