using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace MnemoApp.Core.Services
{
    public sealed class ToastService : IToastService
    {
        private readonly ObservableCollection<ToastNotification> _passive = new();
        private readonly ReadOnlyObservableCollection<ToastNotification> _roPassive;
        private readonly ObservableCollection<ToastNotification> _status = new();
        private readonly ReadOnlyObservableCollection<ToastNotification> _roStatus;

        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource> _timers = new();

        public ToastService()
        {
            _roPassive = new ReadOnlyObservableCollection<ToastNotification>(_passive);
            _roStatus = new ReadOnlyObservableCollection<ToastNotification>(_status);
        }

        public ReadOnlyObservableCollection<ToastNotification> PassiveToasts => _roPassive;
        public ReadOnlyObservableCollection<ToastNotification> StatusToasts => _roStatus;

        public int MaxPassive { get; set; } = 5;

        public Guid Show(string title, string? message = null, ToastType type = ToastType.Info, TimeSpan? duration = null, bool dismissable = true)
        {
            var toast = new ToastNotification
            {
                Title = title,
                Message = message,
                Type = type,
                IsStatus = false,
                Dismissable = dismissable,
                Duration = duration ?? TimeSpan.FromSeconds(2) // Use default duration if null
            };

            // Enforce max only for passive
            while (_passive.Count >= MaxPassive)
            {
                var oldestNonIndefinite = _passive
                    .Where(t => !t.IsIndefinite)
                    .OrderBy(t => t.CreatedAt)
                    .FirstOrDefault();
                var oldest = oldestNonIndefinite ?? _passive.OrderBy(t => t.CreatedAt).FirstOrDefault();
                if (oldest == null) break;
                RemovePassive(oldest.Id);
            }

            RunOnUIThread(() =>
            {
                // Insert at top so newest passive toast appears above older ones
                _passive.Insert(0, toast);
                StartTimerIfNeeded(toast);
            });
            return toast.Id;
        }

        public Guid ShowStatus(string title, string? message = null, ToastType type = ToastType.Process, bool dismissable = true, double? initialProgress = null, string? progressText = null)
        {
            var toast = new ToastNotification
            {
                Title = title,
                Message = message,
                Type = type,
                IsStatus = true,
                Dismissable = dismissable,
                Duration = null,
                Progress = initialProgress,
                ProgressText = progressText
            };
            // Status toasts should always be kept at the bottom of the panel → present last in collection
            RunOnUIThread(() => _status.Add(toast));
            return toast.Id;
        }

        public bool TryUpdateStatus(Guid id, double? progress = null, string? progressText = null, string? title = null, string? message = null, ToastType? type = null)
        {
            var toast = _status.FirstOrDefault(t => t.Id == id);
            if (toast == null) return false;

            RunOnUIThread(() =>
            {
                if (title != null) toast.Title = title;
                if (message != null) toast.Message = message;
                if (type != null) toast.Type = type.Value;
                if (progress != null) toast.Progress = progress;
                if (progressText != null) toast.ProgressText = progressText;
            });
            return true;
        }

        public bool CompleteStatus(Guid id)
        {
            var toast = _status.FirstOrDefault(t => t.Id == id);
            if (toast == null) return false;
            // Remove status and do not convert to passive unless explicitly shown again by the caller
            RunOnUIThread(() => _status.Remove(toast));
            return true;
        }

        public bool AttachTask(Guid toastId, Guid taskId)
        {
            var toast = _status.FirstOrDefault(t => t.Id == toastId);
            if (toast == null) return false;
            RunOnUIThread(() => toast.TaskId = taskId);
            return true;
        }

        public bool Remove(Guid id)
        {
            return RemovePassive(id) || RemoveStatus(id);
        }

        public bool RemovePassive(Guid id)
        {
            var toast = _passive.FirstOrDefault(t => t.Id == id);
            if (toast == null) return false;
            RunOnUIThread(() => _passive.Remove(toast));
            StopTimer(id);
            return true;
        }

        public bool RemoveStatus(Guid id)
        {
            var toast = _status.FirstOrDefault(t => t.Id == id);
            if (toast == null) return false;
            RunOnUIThread(() => _status.Remove(toast));
            StopTimer(id);
            return true;
        }

        public void Clear()
        {
            RunOnUIThread(() =>
            {
                foreach (var t in _passive.ToList()) RemovePassive(t.Id);
                foreach (var t in _status.ToList()) RemoveStatus(t.Id);
            });
        }

        private void StartTimerIfNeeded(ToastNotification toast)
        {
            if (toast.IsStatus) return;
            if (toast.IsIndefinite) return;

            var cts = new CancellationTokenSource();
            _timers[toast.Id] = cts;
            _ = AutoRemoveAsync(toast.Id, toast.Duration!.Value, cts.Token);
        }

        private async Task AutoRemoveAsync(Guid id, TimeSpan delay, CancellationToken token)
        {
            try
            {
                await Task.Delay(delay, token);
                if (!token.IsCancellationRequested)
                {
                    RemovePassive(id);
                }
            }
            catch (TaskCanceledException) { }
        }

        private void StopTimer(Guid id)
        {
            if (_timers.TryRemove(id, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        private static void RunOnUIThread(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.Post(action);
        }
    }
}


