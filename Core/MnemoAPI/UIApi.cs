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
        public TopbarApi topbar { get; }
        public OverlayApi overlay { get; }

        public UIApi(IThemeService themeService, ITopbarService topbarService, IOverlayService overlayService)
        {
            themes = new ThemeApi(themeService);
            topbar = new TopbarApi(topbarService);
            overlay = new OverlayApi(overlayService);
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
}