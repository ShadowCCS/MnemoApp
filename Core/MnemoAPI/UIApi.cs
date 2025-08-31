using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using MnemoApp.Core.Overlays;
using MnemoApp.Core.Services;

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

        public UIApi(IThemeService themeService, ITopbarService topbarService, IOverlayService overlayService)
        {
            themes = new ThemeApi(themeService);
            var loc = Core.ApplicationHost.Services.GetService(typeof(ILocalizationService)) as ILocalizationService;
            if (loc == null)
            {
                loc = new LocalizationService();
            }
            language = new LanguageApi(loc);
            topbar = new TopbarApi(topbarService);
            overlay = new OverlayApi(overlayService);
            var toastService = Core.ApplicationHost.Services.GetService(typeof(IToastService)) as IToastService;
            if (toastService == null)
            {
                // Fallback for design-time
                toastService = new ToastService();
            }
            toast = new ToastApi(toastService);
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
    }

    public class ToastApi
    {
        private readonly IToastService _toastService;

        public ToastApi(IToastService toastService)
        {
            _toastService = toastService;
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
    }
}