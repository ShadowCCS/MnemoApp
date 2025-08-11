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

        public UIApi(IThemeService themeService, ITopbarService topbarService)
        {
            themes = new ThemeApi(themeService);
            topbar = new TopbarApi(topbarService);
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
}