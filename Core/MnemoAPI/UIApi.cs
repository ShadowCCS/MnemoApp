using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using MnemoApp.Core.Overlays;
using MnemoApp.Core.Services;
using MnemoApp.Core.Models;
using MnemoApp.Core.Tasks.Models;
using MnemoApp.Core.Tasks.Services;
using MnemoApp.UI.Components.Overlays;

namespace MnemoApp.Core.MnemoAPI
{
    /// <summary>
    /// UI-related API endpoints
    /// </summary>
    public class UIApi
    {
        public ThemeApi themes { get; }
        public LanguageApi language { get; }
        public TopbarApi topbar { get; }
        public ToastApi toast { get; }
        public OverlayApi overlay { get; }
        public DropdownApi dropdown { get; }
        public LoadingOverlayApi loading { get; }

        public UIApi(
            IThemeService themeService, 
            ITopbarService topbarService, 
            IOverlayService overlayService,
            ILocalizationService localizationService,
            IToastService toastService,
            IDropdownItemRegistry dropdownRegistry,
            ITaskSchedulerService taskScheduler)
        {
            themes = new ThemeApi(themeService);
            language = new LanguageApi(localizationService);
            topbar = new TopbarApi(topbarService);
            overlay = new OverlayApi(overlayService);
            toast = new ToastApi(toastService, taskScheduler, overlayService);
            dropdown = new DropdownApi(dropdownRegistry, overlayService);
            loading = new LoadingOverlayApi(overlayService, taskScheduler);
        }
    }

    /// <summary>
    /// Theme-related API endpoints
    /// </summary>
    public class ThemeApi
    {
        private readonly IThemeService _themeService;

        public ThemeApi(IThemeService themeService)
        {
            _themeService = themeService;
            _themeService.StartWatching();
        }


        public async System.Threading.Tasks.Task<System.Collections.Generic.List<ThemeManifest>> getAllThemes()
        {
            return await _themeService.GetAllThemesAsync();
        }

        public async System.Threading.Tasks.Task<ThemeManifest?> getTheme(string name)
        {
            return await _themeService.GetThemeAsync(name);
        }

        public async System.Threading.Tasks.Task<bool> setTheme(string name)
        {
            return await _themeService.SetThemeAsync(name);
        }

        public ThemeManifest? getCurrentTheme()
        {
            return _themeService.GetCurrentTheme();
        }

        public async System.Threading.Tasks.Task<bool> applyTheme(string name)
        {
            return await _themeService.ApplyThemeAsync(name);
        }

        // New endpoints
        public async System.Threading.Tasks.Task<MnemoApp.Core.Services.ThemeManifest> import(string sourceDirectory)
            => await _themeService.ImportThemeAsync(sourceDirectory);

        public async System.Threading.Tasks.Task export(string themeName, string destinationDirectory)
            => await _themeService.ExportThemeAsync(themeName, destinationDirectory);

        public void startWatching() => _themeService.StartWatching();
        public void stopWatching() => _themeService.StopWatching();
    }

    /// <summary>
    /// Language-related API endpoints
    /// </summary>
    public class LanguageApi
    {
        private readonly ILocalizationService _localizationService;

        public LanguageApi(ILocalizationService localizationService)
        {
            _localizationService = localizationService;
        }

        public string getCurrentLanguage() => _localizationService.CurrentLanguage;

        public async System.Threading.Tasks.Task<bool> setLanguage(string code)
        {
            return await _localizationService.SetLanguageAsync(code);
        }

        public async System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<MnemoApp.Core.Services.LanguageManifest>> getAvailableLanguages()
        {
            return await _localizationService.GetAvailableLanguagesAsync();
        }

        public string get(string ns, string key)
        {
            return _localizationService.T(ns, key);
        }

        public MnemoApp.Core.Services.LanguageManifest? getCurrentLanguageManifest() => _localizationService.GetCurrentLanguageManifest();

        public void registerLanguageJson(string code, string json)
        {
            _localizationService.RegisterLanguageJson(code, json);
        }

        public void registerNamespace(string code, string ns, System.Collections.Generic.IReadOnlyDictionary<string, string> entries)
        {
            _localizationService.RegisterNamespace(code, ns, entries);
        }
    }

    public class TopbarApi
    {
        private readonly ITopbarService _topbarService;

        public TopbarApi(ITopbarService topbarService)
        {
            _topbarService = topbarService;
        }

        public System.Collections.ObjectModel.ReadOnlyObservableCollection<ITopbarItem> items => _topbarService.Items;

        public System.Guid addButton(string iconPath, object? stroke = null, bool notification = false, int order = 0, System.Windows.Input.ICommand? command = null, string? toolTip = null)
        {
            var model = new TopbarButtonModel
            {
                IconPath = iconPath,
                Notification = notification,
                Order = order,
                Command = command,
                ToolTip = toolTip
            };
            return _topbarService.AddButton(model);
        }

        public System.Guid addCustom(Avalonia.Controls.Control control, int order = 0)
        {
            return _topbarService.AddCustom(control, order);
        }

        public bool remove(System.Guid id) => _topbarService.Remove(id);

        public bool setNotification(System.Guid id, bool notification) => _topbarService.SetNotification(id, notification);

        public System.Guid addSeparator(int order = 0, double height = 24, double thickness = 1)
            => _topbarService.AddSeparator(order, height, thickness);
    }

    public class OverlayApi
    {
        private readonly IOverlayService _overlayService;
        public IOverlayService Service => _overlayService;

        public OverlayApi(IOverlayService overlayService)
        {
            _overlayService = overlayService;
        }

        public ReadOnlyObservableCollection<OverlayInstance> overlays => _overlayService.Overlays;
        public bool hasOverlays => _overlayService.HasOverlays;

        // Simplified show APIs
        public Task<T?> Show<T>(Control control, OverlayOptions? options = null, string? name = null, Guid? parentId = null)
        {
            var _ = _overlayService.CreateOverlayWithTask<T>(control, options, name, parentId);
            return _.task;
        }

        public Task<T?> Show<T>(string xamlPathOrAvaresUri, OverlayOptions? options = null, string? name = null, Guid? parentId = null)
        {
            var _ = _overlayService.CreateOverlayFromXamlWithTask<T>(xamlPathOrAvaresUri, options, name, parentId);
            return _.task;
        }

        // Legacy create/close kept for flexibility
        public Guid CreateOverlay(string xamlPathOrAvaresUri, OverlayOptions? options = null, string? name = null, Guid? parentId = null)
            => _overlayService.CreateOverlayFromXaml(xamlPathOrAvaresUri, options, name, parentId);
        public Guid CreateOverlay(Control control, OverlayOptions? options = null, string? name = null, Guid? parentId = null)
            => _overlayService.CreateOverlay(control, options, name, parentId);
        public Task<T?> CreateOverlayAsync<T>(Control control, OverlayOptions? options = null, string? name = null, Guid? parentId = null)
            => _overlayService.CreateOverlayAsync<T>(control, options, name, parentId);
        public Task<T?> CreateOverlayAsync<T>(string xamlPathOrAvaresUri, OverlayOptions? options = null, string? name = null, Guid? parentId = null)
            => _overlayService.CreateOverlayFromXamlAsync<T>(xamlPathOrAvaresUri, options, name, parentId);

        public bool CloseOverlay(Guid id, object? result = null) => _overlayService.CloseOverlay(id, result);
        public bool CloseOverlay(string name, object? result = null) => _overlayService.CloseOverlay(name, result);
        public void CloseAllOverlays() => _overlayService.CloseAllOverlays();

        // Convenience: Dialog
        public Task<string?> CreateDialog(string title, string description, string primaryText, string secondaryText,
            OverlayOptions? options = null, string? name = null)
        {
            var dialog = new UI.Components.Overlays.DialogOverlay
            {
                Title = title,
                Description = description,
                PrimaryText = primaryText,
                SecondaryText = secondaryText,
            };

            var created = _overlayService.CreateOverlayWithTask<string?>(dialog, options, name);
            dialog.OnChoose = choice => _overlayService.CloseOverlay(created.id, choice);
            return created.task;
        }

        // Convenience: Options Dropdown (legacy - use dropdown.ShowWithBuilder instead)
        public void OptionsDropdown(Control anchorControl, Action<IList<DropdownItemBase>> configureItems, OverlayOptions? options = null, string? name = null)
        {
            var items = new List<DropdownItemBase>();
            configureItems(items);

            var dropdown = new UI.Components.Overlays.DropdownOverlay();
            dropdown.SetItems(items, anchorControl);

            var dropdownOptions = options ?? new OverlayOptions
            {
                ShowBackdrop = true,
                BackdropOpacity = 0.0,
                CloseOnOutsideClick = true,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
            };

            _overlayService.CreateOverlay(dropdown, dropdownOptions, name);
        }

        // Convenience: Notification Dropdown (legacy - use dropdown.ShowWithBuilder instead)
        public void NotificationDropdown(Control anchorControl, Action<IList<DropdownItemBase>> configureItems, OverlayOptions? options = null, string? name = null)
        {
            var items = new List<DropdownItemBase>();
            configureItems(items);

            var dropdown = new UI.Components.Overlays.DropdownOverlay();
            dropdown.SetItems(items, anchorControl);

            var dropdownOptions = options ?? new OverlayOptions
            {
                ShowBackdrop = true,
                BackdropOpacity = 0.0,
                CloseOnOutsideClick = true,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
            };

            _overlayService.CreateOverlay(dropdown, dropdownOptions, name);
        }
    }

    public class ToastApi
    {
        private readonly IToastService _toastService;
        public IToastService Service => _toastService;
        private readonly TaskToastService _taskToastService;
        private readonly ITaskSchedulerService _taskScheduler;

        public ToastApi(IToastService toastService, ITaskSchedulerService taskScheduler, IOverlayService overlayService)
        {
            _toastService = toastService;
            _taskScheduler = taskScheduler;
            _taskToastService = new TaskToastService(_toastService, taskScheduler, overlayService);
        }

        public System.Collections.ObjectModel.ReadOnlyObservableCollection<ToastNotification> passive => _toastService.PassiveToasts;
        public System.Collections.ObjectModel.ReadOnlyObservableCollection<ToastNotification> status => _toastService.StatusToasts;

        public System.Guid show(string title, string? message = null, ToastType type = ToastType.Info, System.TimeSpan? duration = null, bool dismissable = true)
            => _toastService.Show(title, message, type, duration, dismissable);

        public System.Guid showStatus(string title, string? message = null, ToastType type = ToastType.Process, bool dismissable = true, double? initialProgress = null, string? progressText = null)
            => _toastService.ShowStatus(title, message, type, dismissable, initialProgress, progressText);

        public bool updateStatus(System.Guid id, double? progress = null, string? progressText = null, string? title = null, string? message = null, ToastType? type = null)
            => _toastService.TryUpdateStatus(id, progress, progressText, title, message, type);

        public bool completeStatus(System.Guid id) => _toastService.CompleteStatus(id);

        public bool remove(System.Guid id) => _toastService.Remove(id);

        public void clear() => _toastService.Clear();

        // Task integration methods
        public System.Guid showForTask(System.Guid taskId, bool showProgress = true)
        {
            var task = _taskScheduler.GetTask(taskId);
            if (task == null)
                throw new ArgumentException($"Task with ID {taskId} not found", nameof(taskId));

            // Check if a toast already exists for this task
            var existingToastId = _taskToastService.GetToastIdForTask(taskId);
            if (existingToastId.HasValue)
            {
                // Update existing toast if needed
                _taskToastService.UpdateTaskToast(task);
                _toastService.AttachTask(existingToastId.Value, task.Id);
                return existingToastId.Value;
            }

            // Create new toast if none exists
            var id = _taskToastService.CreateTaskToast(task, showProgress);
            _toastService.AttachTask(id, task.Id);
            return id;
        }

        public bool updateForTask(System.Guid taskId)
        {
            var task = _taskScheduler.GetTask(taskId);
            if (task == null) return false;

            return _taskToastService.UpdateTaskToast(task);
        }

        public bool removeForTask(System.Guid taskId)
        {
            return _taskToastService.RemoveTaskToast(taskId);
        }

        public System.Guid? getToastIdForTask(System.Guid taskId)
        {
            return _taskToastService.GetToastIdForTask(taskId);
        }
    }

    public class DropdownApi
    {
        private readonly IDropdownItemRegistry _registry;
        private readonly IOverlayService _overlayService;

        public DropdownApi(IDropdownItemRegistry registry, IOverlayService overlayService)
        {
            _registry = registry;
            _overlayService = overlayService;
        }

        /// <summary>
        /// Register a dropdown item for reuse across the application
        /// </summary>
        public void RegisterItem(DropdownType dropdownType, DropdownItemBase item)
        {
            _registry.RegisterItem(dropdownType, item);
        }

        /// <summary>
        /// Register multiple dropdown items
        /// </summary>
        public void RegisterItems(DropdownType dropdownType, IEnumerable<DropdownItemBase> items)
        {
            _registry.RegisterItems(dropdownType, items);
        }

        /// <summary>
        /// Show dropdown with registered items + custom items
        /// </summary>
        public Guid Show(Control anchorControl, DropdownType dropdownType, IEnumerable<DropdownItemBase>? additionalItems = null, string? category = null, OverlayOptions? options = null, string? name = null)
        {
            var registeredItems = string.IsNullOrEmpty(category) 
                ? _registry.GetItems(dropdownType)
                : _registry.GetItems(dropdownType, category);

            var allItems = new List<DropdownItemBase>(registeredItems);
            if (additionalItems != null)
            {
                allItems.AddRange(additionalItems);
                allItems.Sort((a, b) => a.Order.CompareTo(b.Order));
            }

            var dropdown = new DropdownOverlay();
            dropdown.SetItems(allItems, anchorControl);

            var dropdownOptions = options ?? new OverlayOptions
            {
                ShowBackdrop = true,
                BackdropOpacity = 0.0, // Invisible backdrop for click detection
                CloseOnOutsideClick = true,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
            };

            return _overlayService.CreateOverlay(dropdown, dropdownOptions, name);
        }

        /// <summary>
        /// Show dropdown with only custom items (legacy compatibility)
        /// </summary>
        public Guid ShowCustom(Control anchorControl, IEnumerable<DropdownItemBase> items, OverlayOptions? options = null, string? name = null)
        {
            return Show(anchorControl, DropdownType.Options, items, null, options, name);
        }

        /// <summary>
        /// Show dropdown using builder pattern
        /// </summary>
        public Guid ShowWithBuilder(Control anchorControl, Action<IList<DropdownItemBase>> configureItems, DropdownType dropdownType = DropdownType.Options, string? category = null, OverlayOptions? options = null, string? name = null)
        {
            var customItems = new List<DropdownItemBase>();
            configureItems(customItems);
            return Show(anchorControl, dropdownType, customItems, category, options, name);
        }

        /// <summary>
        /// Get registered items (for inspection/debugging)
        /// </summary>
        public IEnumerable<DropdownItemBase> GetRegisteredItems(DropdownType dropdownType, string? category = null)
        {
            return string.IsNullOrEmpty(category) 
                ? _registry.GetItems(dropdownType)
                : _registry.GetItems(dropdownType, category);
        }

        /// <summary>
        /// Remove a registered item
        /// </summary>
        public bool RemoveItem(DropdownType dropdownType, string itemId)
        {
            return _registry.RemoveItem(dropdownType, itemId);
        }

        /// <summary>
        /// Clear all registered items for a dropdown type
        /// </summary>
        public void ClearItems(DropdownType dropdownType)
        {
            _registry.ClearItems(dropdownType);
        }
    }
}