using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Windows.Input;
using MnemoApp.Core.Extensions.Models;
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Core.AI.Services;
using MnemoApp.Core.Services.FileProcessing;
using MnemoApp.Core.Storage;
using MnemoApp.Core.Navigation;
using MnemoApp.Core.Services;
using MnemoApp.Core.Common;
using MnemoApp.Data.Runtime;
using MnemoApp.UI.Components.Sidebar;
using MnemoApp.Core.Extensions.Services;

namespace MnemoApp.Core.Extensions
{
    /// <summary>
    /// Implementation of extension context
    /// </summary>
    internal class ExtensionContext : IExtensionContext, IDisposable
    {
        private readonly IMnemoAPI _api;
        private readonly IServiceProvider _hostServices;
        private readonly Func<ExtensionPermission, Task<bool>> _permissionRequestHandler;
        private readonly ExtensionApiHelper _apiHelper;
        private IServiceProvider? _extensionServices;

        public ExtensionContext(
            ExtensionMetadata metadata,
            IMnemoAPI api,
            IServiceProvider hostServices,
            Func<ExtensionPermission, Task<bool>> permissionRequestHandler,
            ExtensionApiHelper apiHelper)
        {
            Metadata = metadata;
            _api = api;
            _hostServices = hostServices;
            _permissionRequestHandler = permissionRequestHandler;
            _apiHelper = apiHelper;
            
            Logger = new ExtensionLogger(metadata.Manifest.Name);
            
            // Setup data directory
            DataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MnemoApp",
                "Extensions",
                metadata.Manifest.Name,
                "Data"
            );
            
            if (!Directory.Exists(DataDirectory))
            {
                Directory.CreateDirectory(DataDirectory);
            }
            
            StoragePrefix = $"ext:{metadata.Manifest.Name}:";
        }

        public ExtensionMetadata Metadata { get; }
        
        public IMnemoAPI API => new PermissionWrappedAPI(_api, this);
        
        public IServiceProvider Services => _hostServices;
        
        /// <summary>
        /// Extension's own service provider (for extension services)
        /// </summary>
        internal IServiceProvider ServiceProvider => _extensionServices ?? _hostServices;
        
        /// <summary>
        /// Set the extension's service provider
        /// </summary>
        internal void SetExtensionServiceProvider(IServiceProvider serviceProvider)
        {
            _extensionServices = serviceProvider;
        }
        
        public IExtensionLogger Logger { get; }
        
        public ExtensionApiHelper ApiHelper => _apiHelper;
        
        public string DataDirectory { get; }
        
        public string StoragePrefix { get; }
        
        public bool HasPermission(ExtensionPermission permission)
        {
            // FullTrust grants all permissions
            if (Metadata.GrantedPermissions.HasFlag(ExtensionPermission.FullTrust))
                return true;
                
            return Metadata.GrantedPermissions.HasFlag(permission);
        }
        
        public Task<bool> RequestPermissionAsync(ExtensionPermission permission)
        {
            return _permissionRequestHandler(permission);
        }

        public void Dispose()
        {
            // Dispose of the logger if it implements IDisposable
            if (Logger is IDisposable loggerDisposable)
            {
                loggerDisposable.Dispose();
            }
        }
    }
    
    /// <summary>
    /// Permission-wrapped API proxy for extensions
    /// </summary>
    internal class PermissionWrappedAPI : IMnemoAPI
    {
        private readonly IMnemoAPI _api;
        private readonly IExtensionContext _context;

        public PermissionWrappedAPI(IMnemoAPI api, IExtensionContext context)
        {
            _api = api;
            _context = context;
        }

        // Pass through properties that don't need permission checks
        public AIApi ai { get => _api.ai; set => _api.ai = value; }
        public ExtensionApi extensions { get => _api.extensions; set => _api.extensions = value; }
        public FileApi files { get => _api.files; set => _api.files = value; }
        public LaTeXApi latex { get => _api.latex; set => _api.latex = value; }
        public SettingsApi settings { get => _api.settings; set => _api.settings = value; }
        public SystemApi system { get => _api.system; set => _api.system = value; }
        public TaskApi tasks { get => _api.tasks; set => _api.tasks = value; }
        public UIApi ui { get => _api.ui; set => _api.ui = value; }
        public MnemoApp.Data.Packaged.MnemoStorageManager storage { get => _api.storage; set => _api.storage = value; }

        // Data API (return underlying to match interface; permission enforcement deferred)
        public MnemoDataApi data { get => _api.data; set => _api.data = value; }

        // Wrap navigation service with permission checks
        public INavigationService navigate { get => new PermissionWrappedNavigation(_api.navigate, _context); set => _api.navigate = value; }

        // Sidebar service needs permission checks for registration
        public ISidebarService sidebar { get => new PermissionWrappedSidebar(_api.sidebar, _context); set => _api.sidebar = value; }
    }

    /// <summary>
    /// Permission-wrapped data API for extensions
    /// </summary>
    internal class PermissionWrappedDataApi
    {
        private readonly MnemoDataApi _data;
        private readonly IExtensionContext _context;

        public PermissionWrappedDataApi(MnemoDataApi data, IExtensionContext context)
        {
            _data = data;
            _context = context;
        }

        public T? GetProperty<T>(string key, MnemoApp.Data.Runtime.StorageScope scope = MnemoApp.Data.Runtime.StorageScope.Runtime)
        {
            // Always allow reading extension's own storage
            if (key.StartsWith(_context.StoragePrefix))
            {
                return _data.GetProperty<T>(key, scope);
            }

            // For other keys, check file access permission
            if (!_context.HasPermission(ExtensionPermission.FileAccess) &&
                !_context.HasPermission(ExtensionPermission.FullTrust))
            {
                _context.Logger.LogWarning($"Extension '{_context.Metadata.Manifest.Name}' attempted to read restricted data key '{key}'");
                return default;
            }

            return _data.GetProperty<T>(key, scope);
        }

        public void SetProperty<T>(string key, T value, MnemoApp.Data.Runtime.StorageScope scope = MnemoApp.Data.Runtime.StorageScope.Runtime)
        {
            // Check if extension has file access permission for general storage
            if (!_context.HasPermission(ExtensionPermission.FileAccess) &&
                !_context.HasPermission(ExtensionPermission.FullTrust))
            {
                throw new UnauthorizedAccessException($"Extension '{_context.Metadata.Manifest.Name}' does not have permission to write data");
            }

            // Allow writing to extension's own storage prefix without additional checks
            if (key.StartsWith(_context.StoragePrefix))
            {
                _data.SetProperty(key, value, scope);
                return;
            }

            // For other keys, require explicit file access permission
            if (!_context.HasPermission(ExtensionPermission.FileAccess) &&
                !_context.HasPermission(ExtensionPermission.FullTrust))
            {
                throw new UnauthorizedAccessException($"Extension '{_context.Metadata.Manifest.Name}' does not have permission to write to key '{key}'");
            }

            _data.SetProperty(key, value, scope);
        }
    }

    /// <summary>
    /// Permission-wrapped navigation service
    /// </summary>
    internal class PermissionWrappedNavigation : INavigationService
    {
        private readonly INavigationService _navigation;
        private readonly IExtensionContext _context;

        public PermissionWrappedNavigation(INavigationService navigation, IExtensionContext context)
        {
            _navigation = navigation;
            _context = context;
        }

        public void Navigate<TViewModel>() where TViewModel : ViewModelBase
        {
            // Allow navigation if extension has UI access or full trust
            if (!_context.HasPermission(ExtensionPermission.UIAccess) &&
                !_context.HasPermission(ExtensionPermission.FullTrust))
            {
                _context.Logger.LogWarning($"Extension '{_context.Metadata.Manifest.Name}' attempted to navigate without UI permission");
                return;
            }

            _navigation.Navigate<TViewModel>();
        }

        public void Navigate(object viewModelInstance)
        {
            // Allow navigation if extension has UI access or full trust
            if (!_context.HasPermission(ExtensionPermission.UIAccess) &&
                !_context.HasPermission(ExtensionPermission.FullTrust))
            {
                _context.Logger.LogWarning($"Extension '{_context.Metadata.Manifest.Name}' attempted to navigate without UI permission");
                return;
            }

            _navigation.Navigate(viewModelInstance);
        }

        public void Navigate(object viewModelInstance, string breadcrumbTitle, bool clearBreadcrumbs = false)
        {
            // Allow navigation if extension has UI access or full trust
            if (!_context.HasPermission(ExtensionPermission.UIAccess) &&
                !_context.HasPermission(ExtensionPermission.FullTrust))
            {
                _context.Logger.LogWarning($"Extension '{_context.Metadata.Manifest.Name}' attempted to navigate without UI permission");
                return;
            }

            _navigation.Navigate(viewModelInstance, breadcrumbTitle, clearBreadcrumbs);
        }

        public void NavigateToBreadcrumb(BreadcrumbItem breadcrumb)
        {
            // Allow navigation if extension has UI access or full trust
            if (!_context.HasPermission(ExtensionPermission.UIAccess) &&
                !_context.HasPermission(ExtensionPermission.FullTrust))
            {
                _context.Logger.LogWarning($"Extension '{_context.Metadata.Manifest.Name}' attempted to navigate without UI permission");
                return;
            }

            _navigation.NavigateToBreadcrumb(breadcrumb);
        }

        public void GoBack()
        {
            _navigation.GoBack();
        }

        public bool CanGoBack => _navigation.CanGoBack;

        public ICommand GoBackCommand => _navigation.GoBackCommand;

        public ViewModelBase? CurrentViewModel => _navigation.CurrentViewModel;

        public ObservableCollection<BreadcrumbItem> Breadcrumbs => _navigation.Breadcrumbs;

        public event Action<ViewModelBase>? ViewModelChanged
        {
            add => _navigation.ViewModelChanged += value;
            remove => _navigation.ViewModelChanged -= value;
        }

        public event Action? CanGoBackChanged
        {
            add => _navigation.CanGoBackChanged += value;
            remove => _navigation.CanGoBackChanged -= value;
        }
    }

    /// <summary>
    /// Permission-wrapped sidebar service
    /// </summary>
    internal class PermissionWrappedSidebar : ISidebarService
    {
        private readonly ISidebarService _sidebar;
        private readonly IExtensionContext _context;

        public PermissionWrappedSidebar(ISidebarService sidebar, IExtensionContext context)
        {
            _sidebar = sidebar;
            _context = context;
        }

        public ObservableCollection<SidebarCategory> Categories => _sidebar.Categories;

        public void Register(string title, Type viewModelType, string categoryName, string iconPath = "")
        {
            // Require UI access permission for sidebar registration
            if (!_context.HasPermission(ExtensionPermission.UIAccess) &&
                !_context.HasPermission(ExtensionPermission.FullTrust))
            {
                _context.Logger.LogWarning($"Extension '{_context.Metadata.Manifest.Name}' attempted to register sidebar item without UI permission");
                return;
            }

            _sidebar.Register(title, viewModelType, categoryName, iconPath);
        }

        public void Unregister(string title, string categoryName)
        {
            _sidebar.Unregister(title, categoryName);
        }

        public SidebarCategory? GetCategory(string categoryName)
        {
            return _sidebar.GetCategory(categoryName);
        }

        public SidebarItem? GetItem(string title, string categoryName)
        {
            return _sidebar.GetItem(title, categoryName);
        }

        public void ClearAll()
        {
            _sidebar.ClearAll();
        }

        public event Action<SidebarCategory>? CategoryAdded
        {
            add => _sidebar.CategoryAdded += value;
            remove => _sidebar.CategoryAdded -= value;
        }

        public event Action<SidebarItem, SidebarCategory>? ItemAdded
        {
            add => _sidebar.ItemAdded += value;
            remove => _sidebar.ItemAdded -= value;
        }

        public event Action<SidebarItem, SidebarCategory>? ItemRemoved
        {
            add => _sidebar.ItemRemoved += value;
            remove => _sidebar.ItemRemoved -= value;
        }
    }


    /// <summary>
    /// Permission-wrapped Topbar API that tracks topbar buttons for cleanup
    /// </summary>
    internal class PermissionWrappedTopbarApi
    {
        private readonly TopbarApi _topbarApi;
        private readonly IExtensionContext _context;

        public PermissionWrappedTopbarApi(TopbarApi topbarApi, IExtensionContext context)
        {
            _topbarApi = topbarApi;
            _context = context;
        }

        public System.Collections.ObjectModel.ReadOnlyObservableCollection<ITopbarItem> items => _topbarApi.items;

        public System.Guid addButton(string iconPath, object? stroke = null, bool notification = false, int order = 0, System.Windows.Input.ICommand? command = null, string? toolTip = null)
        {
            // Check UI permission
            if (!_context.HasPermission(ExtensionPermission.UIAccess) &&
                !_context.HasPermission(ExtensionPermission.FullTrust))
            {
                _context.Logger.LogWarning($"Extension '{_context.Metadata.Manifest.Name}' attempted to add topbar button without UI permission");
                return Guid.Empty;
            }

            var id = _topbarApi.addButton(iconPath, stroke, notification, order, command, toolTip);
            
            // Track the UI component for cleanup
            if (id != Guid.Empty)
            {
                TrackUIComponent(id);
            }
            
            return id;
        }

        public System.Guid addCustom(Avalonia.Controls.Control control, int order = 0)
        {
            // Check UI permission
            if (!_context.HasPermission(ExtensionPermission.UIAccess) &&
                !_context.HasPermission(ExtensionPermission.FullTrust))
            {
                _context.Logger.LogWarning($"Extension '{_context.Metadata.Manifest.Name}' attempted to add custom topbar control without UI permission");
                return Guid.Empty;
            }

            var id = _topbarApi.addCustom(control, order);
            
            // Track the UI component for cleanup
            if (id != Guid.Empty)
            {
                TrackUIComponent(id);
            }
            
            return id;
        }

        public bool remove(System.Guid id)
        {
            var result = _topbarApi.remove(id);
            if (result)
            {
                // Remove from tracking
                RemoveUIComponent(id);
            }
            return result;
        }

        public bool setNotification(System.Guid id, bool notification) => _topbarApi.setNotification(id, notification);

        public System.Guid addSeparator(int order = 0, double height = 24, double thickness = 1)
        {
            var id = _topbarApi.addSeparator(order, height, thickness);
            
            // Track the UI component for cleanup
            if (id != Guid.Empty)
            {
                TrackUIComponent(id);
            }
            
            return id;
        }

        private void TrackUIComponent(Guid componentId)
        {
            // Get the extension service to track this component
            var extensionService = _context.Services.GetService(typeof(IExtensionService));
            if (extensionService is ExtensionService extService)
            {
                extService.TrackUIComponent(_context.Metadata.Manifest.Name, componentId);
            }
        }

        private void RemoveUIComponent(Guid componentId)
        {
            // Get the extension service to remove this component from tracking
            var extensionService = _context.Services.GetService(typeof(IExtensionService));
            if (extensionService is ExtensionService extService)
            {
                extService.RemoveUIComponent(_context.Metadata.Manifest.Name, componentId);
            }
        }
    }

    /// <summary>
    /// Logger implementation for extensions
    /// </summary>
    internal class ExtensionLogger : IExtensionLogger
    {
        private readonly string _extensionName;

        public ExtensionLogger(string extensionName)
        {
            _extensionName = extensionName;
        }

        public void LogInfo(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[EXT:{_extensionName}] INFO: {message}");
        }

        public void LogWarning(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[EXT:{_extensionName}] WARN: {message}");
        }

        public void LogError(string message, Exception? exception = null)
        {
            System.Diagnostics.Debug.WriteLine($"[EXT:{_extensionName}] ERROR: {message}");
            if (exception != null)
            {
                System.Diagnostics.Debug.WriteLine($"[EXT:{_extensionName}] EXCEPTION: {exception}");
            }
        }

        public void LogDebug(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[EXT:{_extensionName}] DEBUG: {message}");
        }
    }

}

