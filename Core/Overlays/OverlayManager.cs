using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Threading;

namespace MnemoApp.Core.Overlays
{
    public interface IOverlayService
    {
        ReadOnlyObservableCollection<OverlayInstance> Overlays { get; }
        bool HasOverlays { get; }

        Guid CreateOverlay(Control content, OverlayOptions? options = null, string? name = null, Guid? parentId = null);
        Guid CreateOverlayFromXaml(string xamlPathOrAvaresUri, OverlayOptions? options = null, string? name = null, Guid? parentId = null);

        Task<T?> CreateOverlayAsync<T>(Control content, OverlayOptions? options = null, string? name = null, Guid? parentId = null);
        Task<T?> CreateOverlayFromXamlAsync<T>(string xamlPathOrAvaresUri, OverlayOptions? options = null, string? name = null, Guid? parentId = null);

        (Guid id, Task<T?> task) CreateOverlayWithTask<T>(Control content, OverlayOptions? options = null, string? name = null, Guid? parentId = null);
        (Guid id, Task<T?> task) CreateOverlayFromXamlWithTask<T>(string xamlPathOrAvaresUri, OverlayOptions? options = null, string? name = null, Guid? parentId = null);

        bool CloseOverlay(Guid id, object? result = null);
        bool CloseOverlay(string name, object? result = null);
        void CloseAllOverlays();
    }

    public sealed class OverlayOptions
    {
        public bool ShowBackdrop { get; set; } = true;
        public Color BackdropColor { get; set; } = Colors.Black;
        public IBrush? BackdropBrush { get; set; } = null;
        public double BackdropOpacity { get; set; } = 0.45;
        public bool CloseOnOutsideClick { get; set; } = true;
        public TimeSpan? CloseAfterDelay { get; set; } = null;

        // Layout (optional; default center)
        public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Center;
        public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Center;
        public Thickness Margin { get; set; } = new Thickness(0);
    }

    public sealed class OverlayInstance
    {
        public Guid Id { get; init; }
        public string? Name { get; init; }
        public Guid? ParentId { get; init; }
        public required Control Content { get; init; }
        public required OverlayOptions Options { get; init; }
        public int ZIndex { get; internal set; }

        internal TaskCompletionSource<object?>? ResultSource { get; set; }
    }

    public class OverlayManager : IOverlayService
    {
        private readonly ObservableCollection<OverlayInstance> _overlays = new();
        private readonly ReadOnlyObservableCollection<OverlayInstance> _readonlyOverlays;

        public OverlayManager()
        {
            _readonlyOverlays = new ReadOnlyObservableCollection<OverlayInstance>(_overlays);
        }

        public ReadOnlyObservableCollection<OverlayInstance> Overlays => _readonlyOverlays;
        public bool HasOverlays => _overlays.Count > 0;

        public Guid CreateOverlay(Control content, OverlayOptions? options = null, string? name = null, Guid? parentId = null)
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            var opts = options ?? new OverlayOptions();

            var instance = new OverlayInstance
            {
                Id = Guid.NewGuid(),
                Name = name,
                ParentId = parentId,
                Content = content,
                Options = opts,
                ResultSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously)
            };

            // Assign ZIndex: maintain stack order; child overlays higher than parents
            instance.ZIndex = (_overlays.Count == 0) ? 1000 : _overlays.Max(o => o.ZIndex) + 1;

            // Add to collection on UI thread
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => _overlays.Add(instance));
            }
            else
            {
                _overlays.Add(instance);
            }

            // Auto-close after delay if specified
            if (opts.CloseAfterDelay is TimeSpan delay)
            {
                _ = ScheduleClose(instance.Id, delay);
            }

            return instance.Id;
        }

        public Guid CreateOverlayFromXaml(string xamlPathOrAvaresUri, OverlayOptions? options = null, string? name = null, Guid? parentId = null)
        {
            var control = LoadControlFromXaml(xamlPathOrAvaresUri);
            return CreateOverlay(control, options, name, parentId);
        }

        public async Task<T?> CreateOverlayAsync<T>(Control content, OverlayOptions? options = null, string? name = null, Guid? parentId = null)
        {
            var id = CreateOverlay(content, options, name, parentId);
            var instance = _overlays.First(o => o.Id == id);
            var result = await instance.ResultSource!.Task.ConfigureAwait(false);
            if (result is T typed)
                return typed;
            return default;
        }

        public Task<T?> CreateOverlayFromXamlAsync<T>(string xamlPathOrAvaresUri, OverlayOptions? options = null, string? name = null, Guid? parentId = null)
        {
            var control = LoadControlFromXaml(xamlPathOrAvaresUri);
            return CreateOverlayAsync<T>(control, options, name, parentId);
        }

        public (Guid id, Task<T?> task) CreateOverlayWithTask<T>(Control content, OverlayOptions? options = null, string? name = null, Guid? parentId = null)
        {
            var id = CreateOverlay(content, options, name, parentId);
            var instance = _overlays.First(o => o.Id == id);
            var task = instance.ResultSource!.Task.ContinueWith(t => t.Result is T ok ? ok : default, TaskScheduler.Default);
            return (id, task);
        }

        public (Guid id, Task<T?> task) CreateOverlayFromXamlWithTask<T>(string xamlPathOrAvaresUri, OverlayOptions? options = null, string? name = null, Guid? parentId = null)
        {
            var control = LoadControlFromXaml(xamlPathOrAvaresUri);
            return CreateOverlayWithTask<T>(control, options, name, parentId);
        }

        public bool CloseOverlay(Guid id, object? result = null)
        {
            OverlayInstance? instance = null;
            if (!Dispatcher.UIThread.CheckAccess())
            {
                var mre = new ManualResetEventSlim(false);
                Dispatcher.UIThread.Post(() =>
                {
                    instance = _overlays.FirstOrDefault(o => o.Id == id);
                    if (instance != null)
                    {
                        RemoveOverlayWithChildren(instance);
                    }
                    mre.Set();
                });
                mre.Wait();
            }
            else
            {
                instance = _overlays.FirstOrDefault(o => o.Id == id);
                if (instance != null)
                {
                    RemoveOverlayWithChildren(instance);
                }
            }

            if (instance == null)
                return false;

            instance.ResultSource?.TrySetResult(result);
            return true;
        }

        public bool CloseOverlay(string name, object? result = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var inst = _overlays.LastOrDefault(o => string.Equals(o.Name, name, StringComparison.Ordinal));
            if (inst == null) return false;
            return CloseOverlay(inst.Id, result);
        }

        public void CloseAllOverlays()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(CloseAllOverlays);
                return;
            }

            // Copy list to avoid mutation issues during enumeration
            var snapshot = _overlays.ToList();
            _overlays.Clear();
            foreach (var overlay in snapshot)
            {
                overlay.ResultSource?.TrySetResult(null);
            }
        }

        private void RemoveOverlayWithChildren(OverlayInstance instance)
        {
            // Remove children first (LIFO order)
            var children = _overlays.Where(o => o.ParentId == instance.Id).ToList();
            foreach (var child in children)
            {
                RemoveOverlayWithChildren(child);
            }
            _overlays.Remove(instance);
        }

        private static Control LoadControlFromXaml(string xamlPathOrAvaresUri)
        {
            if (string.IsNullOrWhiteSpace(xamlPathOrAvaresUri))
                throw new ArgumentException("Invalid XAML path", nameof(xamlPathOrAvaresUri));

            // If string looks like a type name, attempt to construct instance
            var type = Type.GetType(xamlPathOrAvaresUri, throwOnError: false);
            if (type != null && typeof(Control).IsAssignableFrom(type))
            {
                return (Control)Activator.CreateInstance(type)!;
            }

            // Normalize to avares URI if needed
            var uri = xamlPathOrAvaresUri.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(xamlPathOrAvaresUri)
                : new Uri($"avares://MnemoApp/{xamlPathOrAvaresUri.TrimStart('/')}");

            var loaded = Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(uri);
            if (loaded is Control ctrl)
                return ctrl;
            throw new InvalidOperationException($"XAML at '{uri}' did not load a Control");
        }

        private async Task ScheduleClose(Guid id, TimeSpan delay)
        {
            await Task.Delay(delay).ConfigureAwait(false);
            // Ignore result; if already closed no-op
            _ = CloseOverlay(id);
        }
    }
}

