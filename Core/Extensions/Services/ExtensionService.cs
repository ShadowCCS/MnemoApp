using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MnemoApp.Core.Extensions.Models;
using MnemoApp.Core.Extensions.Security;
using MnemoApp.Core.Extensions.Packaging;
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Core.Services;
using MnemoApp.Core.Navigation;
using MnemoApp.Core.Overlays;
using MnemoApp.Core.AI.Services;
using MnemoApp.Data.Runtime;

namespace MnemoApp.Core.Extensions.Services
{
    /// <summary>
    /// Manages extension lifecycle
    /// </summary>
    public class ExtensionService : IExtensionService, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IRuntimeStorage _storage;
        private readonly ExtensionLoader _loader;
        private readonly ExtensionUnpacker _unpacker;
        private readonly TrustStore _trustStore;
        private readonly PermissionPromptService _permissionPrompt;
        private readonly DependencyResolver _dependencyResolver;
        private readonly ExtensionApiRouter _apiRouter;
        private readonly SourceBasedExtensionCompiler _sourceCompiler;
        
        private readonly Dictionary<string, ExtensionMetadata> _extensions = new();
        private readonly Dictionary<string, LoadedExtension> _loadedExtensions = new();
        private readonly Dictionary<string, HashSet<Guid>> _extensionUIComponents = new();
        
        private readonly string _userExtensionsPath;
        private readonly string _bundledExtensionsPath;
        
        private bool _initialized = false;

        public ExtensionService(
            IServiceProvider serviceProvider,
            IRuntimeStorage storage,
            PermissionPromptService permissionPrompt)
        {
            _serviceProvider = serviceProvider;
            _storage = storage;
            _permissionPrompt = permissionPrompt;
            _loader = new ExtensionLoader(serviceProvider);
            _unpacker = new ExtensionUnpacker();
            _trustStore = new TrustStore(storage);
            _dependencyResolver = new DependencyResolver();
            _apiRouter = new ExtensionApiRouter(this);
            _sourceCompiler = new SourceBasedExtensionCompiler();
            
            _userExtensionsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MnemoApp",
                "Extensions"
            );
            
            _bundledExtensionsPath = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location ?? System.Reflection.Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory,
                "Modules",
                "Extensions"
            );
        }

        public event EventHandler<ExtensionEventArgs>? ExtensionStateChanged;

        /// <summary>
        /// Get the API router for extension API calls
        /// </summary>
        public ExtensionApiRouter ApiRouter => _apiRouter;

        public async Task InitializeAsync()
        {
            if (_initialized) return;

            System.Diagnostics.Debug.WriteLine("[EXT_SERVICE] Initializing extension service...");

            // Ensure directories exist
            Directory.CreateDirectory(_userExtensionsPath);

            // Discover extensions
            await RefreshExtensionsAsync();

            // Resolve dependencies and determine load order
            var enabledExtensions = _extensions.Values
                .Where(e => e.IsEnabled && e.State == ExtensionState.Unloaded)
                .ToList();

            if (enabledExtensions.Any())
            {
                var resolution = _dependencyResolver.Resolve(enabledExtensions);
                
                if (!resolution.IsSuccess)
                {
                    System.Diagnostics.Debug.WriteLine("[EXT_SERVICE] Dependency resolution errors:");
                    foreach (var error in resolution.Errors)
                    {
                        System.Diagnostics.Debug.WriteLine($"  - {error}");
                    }
                }

                // Load extensions in dependency order
                foreach (var ext in resolution.LoadOrder)
                {
                    await LoadExtensionAsync(ext.Manifest.Name);
                }
            }

            _initialized = true;
            System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Initialized with {_loadedExtensions.Count} extensions loaded");
        }

        public IReadOnlyList<ExtensionMetadata> GetAllExtensions()
        {
            return _extensions.Values.ToList();
        }

        public IReadOnlyList<ExtensionMetadata> GetLoadedExtensions()
        {
            return _loadedExtensions.Values.Select(e => e.Metadata).ToList();
        }

        public ExtensionMetadata? GetExtension(string name)
        {
            _extensions.TryGetValue(name, out var metadata);
            return metadata;
        }

        public IMnemoExtension? GetExtensionInstance(string name)
        {
            _loadedExtensions.TryGetValue(name, out var loaded);
            return loaded?.Instance;
        }

        /// <summary>
        /// Get the extension context for a loaded extension
        /// </summary>
        public IExtensionContext? GetExtensionContext(string extensionName)
        {
            _loadedExtensions.TryGetValue(extensionName, out var loaded);
            return loaded?.Context;
        }

        public async Task<bool> LoadExtensionAsync(string name)
        {
            if (!_extensions.TryGetValue(name, out var metadata))
            {
                System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Extension not found: {name}");
                return false;
            }

            if (_loadedExtensions.ContainsKey(name))
            {
                System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Extension already loaded: {name}");
                return true;
            }

            System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Loading extension: {name}");
            
            var oldState = metadata.State;
            metadata.State = ExtensionState.Loading;
            metadata.LoadErrors.Clear();

            try
            {
                // Validate dependencies
                var dependencyErrors = _dependencyResolver.ValidateDependencies(metadata, _extensions.Values);
                if (dependencyErrors.Any())
                {
                    metadata.State = ExtensionState.Failed;
                    metadata.LoadErrors.AddRange(dependencyErrors);
                    System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Dependency validation failed for {name}");
                    return false;
                }

                // Ensure dependencies are loaded first
                foreach (var dependency in metadata.Manifest.Dependencies)
                {
                    if (!_loadedExtensions.ContainsKey(dependency.Key))
                    {
                        System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Loading dependency: {dependency.Key}");
                        var depLoaded = await LoadExtensionAsync(dependency.Key);
                        if (!depLoaded)
                        {
                            metadata.State = ExtensionState.Failed;
                            metadata.LoadErrors.Add($"Failed to load dependency: {dependency.Key}");
                            return false;
                        }
                    }
                }

                // Determine trust level
                if (metadata.InstallPath.StartsWith(_bundledExtensionsPath, StringComparison.OrdinalIgnoreCase))
                {
                    metadata.TrustLevel = ExtensionTrustLevel.Development;
                }
                else
                {
                    metadata.TrustLevel = _trustStore.GetTrustLevel(name);
                }

                // Handle source-based extensions
                if (metadata.LoadMode == ExtensionLoadMode.SourceBased)
                {
                    System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Compiling source extension: {name}");
                    
                    var needsRecompilation = _sourceCompiler.NeedsRecompilation(metadata.InstallPath, name);
                    if (needsRecompilation)
                    {
                        var compilationResult = await _sourceCompiler.CompileAsync(metadata.InstallPath, name);
                        
                        if (!compilationResult.Success)
                        {
                            metadata.State = ExtensionState.Failed;
                            metadata.LoadErrors.Add($"Compilation failed: {compilationResult.Error}");
                            metadata.LoadErrors.AddRange(compilationResult.Errors);
                            return false;
                        }

                        // Update manifest to use compiled assembly
                        if (compilationResult.AssemblyPath != null)
                        {
                            metadata.Manifest.EntryPoint = compilationResult.AssemblyPath;
                            metadata.LoadMode = ExtensionLoadMode.CompiledAssembly;
                        }
                    }
                    else
                    {
                        // Use cached compiled assembly
                        var cachedAssemblyPath = Path.Combine(
                            Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "MnemoApp",
                                "ExtensionCache"
                            ),
                            $"{name}.dll"
                        );
                        
                        if (File.Exists(cachedAssemblyPath))
                        {
                            // Verify cached assembly is valid before using it
                            try
                            {
                                using var fs = File.OpenRead(cachedAssemblyPath);
                                var assemblyBytes = new byte[fs.Length];
                                fs.Read(assemblyBytes, 0, assemblyBytes.Length);
                                _ = Assembly.Load(assemblyBytes);
                                
                                metadata.Manifest.EntryPoint = cachedAssemblyPath;
                                metadata.LoadMode = ExtensionLoadMode.CompiledAssembly;
                                System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Using cached compiled assembly: {cachedAssemblyPath}");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Cached assembly is corrupted, recompiling: {ex.Message}");
                                
                                // Delete corrupted cache and recompile
                                try
                                {
                                    File.Delete(cachedAssemblyPath);
                                    var pdbPath = Path.Combine(Path.GetDirectoryName(cachedAssemblyPath)!, $"{name}.pdb");
                                    if (File.Exists(pdbPath)) File.Delete(pdbPath);
                                }
                                catch { }
                                
                                var compilationResult = await _sourceCompiler.CompileAsync(metadata.InstallPath, name);
                                
                                if (!compilationResult.Success)
                                {
                                    metadata.State = ExtensionState.Failed;
                                    metadata.LoadErrors.Add($"Compilation failed: {compilationResult.Error}");
                                    metadata.LoadErrors.AddRange(compilationResult.Errors);
                                    return false;
                                }

                                if (compilationResult.AssemblyPath != null)
                                {
                                    metadata.Manifest.EntryPoint = compilationResult.AssemblyPath;
                                    metadata.LoadMode = ExtensionLoadMode.CompiledAssembly;
                                }
                            }
                        }
                        else
                        {
                            metadata.State = ExtensionState.Failed;
                            metadata.LoadErrors.Add("Cached assembly not found, but recompilation was skipped");
                            return false;
                        }
                    }
                }

                // Handle permissions
                var storedPermissions = _trustStore.GetGrantedPermissions(name);
                
                if (storedPermissions == ExtensionPermission.None && metadata.TrustLevel != ExtensionTrustLevel.Development)
                {
                    // Need to prompt for permissions
                    var (approved, granted) = await _permissionPrompt.PromptPermissionsAsync(metadata);
                    
                    if (!approved)
                    {
                        metadata.State = ExtensionState.Failed;
                        metadata.LoadErrors.Add("User denied permissions");
                        return false;
                    }

                    metadata.GrantedPermissions = granted;
                    _trustStore.SetGrantedPermissions(name, granted);
                    _trustStore.SetTrustLevel(name, ExtensionTrustLevel.Trusted);
                }
                else if (metadata.TrustLevel == ExtensionTrustLevel.Development)
                {
                    metadata.GrantedPermissions = ExtensionPermission.FullTrust;
                }
                else
                {
                    metadata.GrantedPermissions = storedPermissions;
                }

                // Load the extension
                var (instance, errors) = await _loader.LoadExtensionAsync(metadata);
                
                if (instance == null || errors.Any())
                {
                    metadata.State = ExtensionState.Failed;
                    metadata.LoadErrors.AddRange(errors);
                    System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Failed to load extension {name}: {string.Join(", ", errors)}");
                    foreach (var error in errors)
                    {
                        System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Load error: {error}");
                    }
                    return false;
                }

                // Get MnemoAPI from service provider
                var api = _serviceProvider.GetRequiredService<IMnemoAPI>();
                
                // Create API helper for extension
                var apiHelper = new ExtensionApiHelper(_apiRouter, name);

                // Create extension context
                var context = new ExtensionContext(
                    metadata,
                    api,
                    _serviceProvider,
                    async (permission) => await RequestRuntimePermissionAsync(name, permission),
                    apiHelper
                );

                // Handle service contributions
                if (instance is IServiceContributor serviceContributor)
                {
                    System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Extension '{name}' is IServiceContributor, registering services...");
                    
                    var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
                    
                    // Register the extension context so extensions can access it
                    services.AddSingleton<IExtensionContext>(context);
                    
                    // Register host services in the extension's service collection
                    // so extension services can depend on host services
                    foreach (var hostService in new[]
                    {
                        typeof(IMnemoAPI),
                        typeof(INavigationService),
                        typeof(ISidebarService),
                        typeof(IThemeService),
                        typeof(ILocalizationService),
                        typeof(IToastService),
                        typeof(IOverlayService),
                        typeof(IAIService),
                    })
                    {
                        var hostServiceInstance = _serviceProvider.GetService(hostService);
                        if (hostServiceInstance != null)
                        {
                            services.AddSingleton(hostService, hostServiceInstance);
                        }
                    }
                    
                    // Let the extension register its services
                    serviceContributor.RegisterServices(services);
                    
                    // Build extension's service provider
                    var extensionServiceProvider = services.BuildServiceProvider();
                    context.SetExtensionServiceProvider(extensionServiceProvider);
                    
                    System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Extension '{name}' services registered");
                }

                // Call OnLoadAsync
                await instance.OnLoadAsync(context);

                // Handle UI contributions
                if (instance is IUIContributor uiContributor)
                {
                    System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Extension '{name}' is IUIContributor, checking permissions...");
                    System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Granted permissions: {metadata.GrantedPermissions}");
                    
                    if (metadata.GrantedPermissions.HasFlag(ExtensionPermission.UIAccess) ||
                        metadata.GrantedPermissions.HasFlag(ExtensionPermission.FullTrust))
                    {
                        System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Calling RegisterUIAsync for extension '{name}'");
                        try
                        {
                            await uiContributor.RegisterUIAsync(context);
                            System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] RegisterUIAsync completed for extension '{name}'");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Error in RegisterUIAsync for extension '{name}': {ex.Message}");
                            System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Stack trace: {ex.StackTrace}");
                            throw;
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Extension '{name}' does not have UI permissions");
                    }
                }

                // Handle API contributions
                if (instance is IApiContributor apiContributor)
                {
                    if (metadata.GrantedPermissions.HasFlag(ExtensionPermission.ApiRegistration) ||
                        metadata.GrantedPermissions.HasFlag(ExtensionPermission.FullTrust))
                    {
                        await apiContributor.RegisterApiAsync(context);
                        System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Extension '{name}' registered API endpoints");
                    }
                }

                // Store loaded extension
                var loadedExt = new LoadedExtension
                {
                    Metadata = metadata,
                    Instance = instance,
                    Context = context
                };
                
                _loadedExtensions[name] = loadedExt;
                
                metadata.State = metadata.IsEnabled ? ExtensionState.Enabled : ExtensionState.Loaded;
                metadata.LastLoadedAt = DateTime.UtcNow;

                // Call OnEnableAsync if enabled
                if (metadata.IsEnabled)
                {
                    await instance.OnEnableAsync();
                }

                ExtensionStateChanged?.Invoke(this, new ExtensionEventArgs
                {
                    Metadata = metadata,
                    OldState = oldState,
                    NewState = metadata.State
                });

                System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Successfully loaded extension: {name}");
                return true;
            }
            catch (Exception ex)
            {
                metadata.State = ExtensionState.Failed;
                metadata.LoadErrors.Add($"Exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Exception loading extension {name}: {ex}");
                System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Exception details: {ex}");
                System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Inner exception: {ex.InnerException}");
                return false;
            }
        }

        public async Task<bool> UnloadExtensionAsync(string name)
        {
            if (!_loadedExtensions.TryGetValue(name, out var loadedExt))
            {
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Unloading extension: {name}");
            
            var oldState = loadedExt.Metadata.State;
            loadedExt.Metadata.State = ExtensionState.Unloading;

            try
            {
                // Call OnDisableAsync first to clean up UI components
                if (loadedExt.Metadata.IsEnabled)
                {
                    await loadedExt.Instance.OnDisableAsync();
                }

                // Force cleanup of all UI components registered by this extension
                await CleanupExtensionUIAsync(name);

                // Unregister API endpoints
                _apiRouter.UnregisterExtension(name);

                // Call OnUnloadAsync for final cleanup
                await loadedExt.Instance.OnUnloadAsync();
                
                // Dispose of the extension instance if it implements IDisposable
                if (loadedExt.Instance is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                // Clear the context to break references
                if (loadedExt.Context is IDisposable contextDisposable)
                {
                    contextDisposable.Dispose();
                }

                // Unload the extension's assembly context
                _loader.UnloadExtension(name);

                _loadedExtensions.Remove(name);
                loadedExt.Metadata.State = ExtensionState.Unloaded;

                ExtensionStateChanged?.Invoke(this, new ExtensionEventArgs
                {
                    Metadata = loadedExt.Metadata,
                    OldState = oldState,
                    NewState = ExtensionState.Unloaded
                });

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Error unloading extension {name}: {ex.Message}");
                loadedExt.Metadata.State = oldState;
                return false;
            }
        }

        public async Task<bool> EnableExtensionAsync(string name)
        {
            if (!_extensions.TryGetValue(name, out var metadata))
            {
                return false;
            }

            metadata.IsEnabled = true;
            SaveExtensionSettings();

            if (!_loadedExtensions.ContainsKey(name))
            {
                return await LoadExtensionAsync(name);
            }

            if (_loadedExtensions.TryGetValue(name, out var loadedExt))
            {
                var oldState = metadata.State;
                metadata.State = ExtensionState.Enabled;
                
                // Re-register UI components if the extension is a UI contributor
                if (loadedExt.Instance is IUIContributor uiContributor)
                {
                    System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Re-registering UI for extension '{name}'");
                    
                    if (metadata.GrantedPermissions.HasFlag(ExtensionPermission.UIAccess) ||
                        metadata.GrantedPermissions.HasFlag(ExtensionPermission.FullTrust))
                    {
                        try
                        {
                            await uiContributor.RegisterUIAsync(loadedExt.Context);
                            System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] UI re-registration completed for extension '{name}'");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Error re-registering UI for extension '{name}': {ex.Message}");
                            throw;
                        }
                    }
                }
                
                await loadedExt.Instance.OnEnableAsync();

                ExtensionStateChanged?.Invoke(this, new ExtensionEventArgs
                {
                    Metadata = metadata,
                    OldState = oldState,
                    NewState = ExtensionState.Enabled
                });
            }

            return true;
        }

        public async Task<bool> DisableExtensionAsync(string name)
        {
            if (!_extensions.TryGetValue(name, out var metadata))
            {
                return false;
            }

            metadata.IsEnabled = false;
            SaveExtensionSettings();

            if (_loadedExtensions.TryGetValue(name, out var loadedExt))
            {
                var oldState = metadata.State;
                metadata.State = ExtensionState.Disabled;
                
                await loadedExt.Instance.OnDisableAsync();

                ExtensionStateChanged?.Invoke(this, new ExtensionEventArgs
                {
                    Metadata = metadata,
                    OldState = oldState,
                    NewState = ExtensionState.Disabled
                });
            }

            return true;
        }

        public async Task<(bool success, string? error)> InstallExtensionAsync(string sourcePath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Installing extension from: {sourcePath}");

                // Check if it's a .mnemoext package or a directory
                bool isPackage = File.Exists(sourcePath) && 
                                Path.GetExtension(sourcePath).Equals(".mnemoext", StringComparison.OrdinalIgnoreCase);

                string tempExtractPath = sourcePath;

                if (isPackage)
                {
                    // Extract package using ExtensionUnpacker
                    var (unpackSuccess, extractedManifest, unpackError) = await _unpacker.UnpackExtensionAsync(sourcePath, _userExtensionsPath);
                    if (!unpackSuccess || extractedManifest == null)
                    {
                        return (false, unpackError ?? "Failed to extract package");
                    }

                    // Refresh extensions to discover the newly installed extension
                    await RefreshExtensionsAsync();
                    return (true, null);
                }
                else if (!Directory.Exists(sourcePath))
                {
                    return (false, "Source path does not exist");
                }

                // Load manifest
                var manifestPath = Path.Combine(sourcePath, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    return (false, "manifest.json not found");
                }

                var manifest = _loader.LoadManifest(manifestPath);
                if (manifest == null)
                {
                    return (false, "Failed to parse manifest.json");
                }

                // Check if already installed
                if (_extensions.ContainsKey(manifest.Name))
                {
                    return (false, $"Extension '{manifest.Name}' is already installed");
                }

                // Copy to user extensions directory
                var destPath = Path.Combine(_userExtensionsPath, manifest.Name);
                if (Directory.Exists(destPath))
                {
                    Directory.Delete(destPath, true);
                }

                CopyDirectory(sourcePath, destPath);

                // Refresh extensions
                await RefreshExtensionsAsync();

                return (true, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Install failed: {ex.Message}");
                return (false, ex.Message);
            }
        }

        public async Task<bool> UninstallExtensionAsync(string name)
        {
            if (!_extensions.TryGetValue(name, out var metadata))
            {
                return false;
            }

            var oldState = metadata.State;

            // Unload if loaded
            if (_loadedExtensions.ContainsKey(name))
            {
                await UnloadExtensionAsync(name);
            }

            // Delete from disk
            try
            {
                if (Directory.Exists(metadata.InstallPath))
                {
                    Directory.Delete(metadata.InstallPath, true);
                }

                // Remove from registry
                _extensions.Remove(name);

                // Remove trust data
                _trustStore.RemoveTrustEntry(name);

                // Fire extension state changed event to notify UI
                ExtensionStateChanged?.Invoke(this, new ExtensionEventArgs
                {
                    Metadata = metadata,
                    OldState = oldState,
                    NewState = ExtensionState.Uninstalled
                });

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Uninstall failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ReloadExtensionAsync(string name)
        {
            await UnloadExtensionAsync(name);
            return await LoadExtensionAsync(name);
        }

        /// <summary>
        /// Force cleanup of all UI components registered by an extension
        /// </summary>
        private async Task CleanupExtensionUIAsync(string extensionName)
        {
            try
            {
                // Get the topbar service and remove tracked items
                var topbarService = _serviceProvider.GetService<ITopbarService>();
                if (topbarService != null && _extensionUIComponents.TryGetValue(extensionName, out var componentIds))
                {
                    foreach (var componentId in componentIds.ToList())
                    {
                        topbarService.Remove(componentId);
                    }
                }

                // Get the sidebar service and remove items by extension name
                var sidebarService = _serviceProvider.GetService<ISidebarService>();
                if (sidebarService != null)
                {
                    // Remove all sidebar items that might belong to this extension
                    // We'll use a naming convention or extension-specific categories
                    var categories = sidebarService.Categories.ToList();
                    foreach (var category in categories)
                    {
                        var itemsToRemove = category.Items.Where(item => 
                            item.Title.Contains(extensionName, StringComparison.OrdinalIgnoreCase) ||
                            category.Name.Contains(extensionName, StringComparison.OrdinalIgnoreCase)
                        ).ToList();
                        
                        foreach (var item in itemsToRemove)
                        {
                            sidebarService.Unregister(item.Title, category.Name);
                        }
                    }
                }

                // Clear the tracking for this extension
                _extensionUIComponents.Remove(extensionName);

                System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Cleaned up UI components for extension: {extensionName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Error cleaning up UI for extension {extensionName}: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Track a UI component for an extension
        /// </summary>
        internal void TrackUIComponent(string extensionName, Guid componentId)
        {
            if (!_extensionUIComponents.ContainsKey(extensionName))
            {
                _extensionUIComponents[extensionName] = new HashSet<Guid>();
            }
            _extensionUIComponents[extensionName].Add(componentId);
        }

        /// <summary>
        /// Remove a UI component from tracking for an extension
        /// </summary>
        internal void RemoveUIComponent(string extensionName, Guid componentId)
        {
            if (_extensionUIComponents.TryGetValue(extensionName, out var components))
            {
                components.Remove(componentId);
            }
        }

        /// <summary>
        /// Dispose of the extension service and unload all extensions
        /// </summary>
        public void Dispose()
        {
            try
            {
                // Unload all loaded extensions
                foreach (var extensionName in _loadedExtensions.Keys.ToList())
                {
                    UnloadExtensionAsync(extensionName).Wait();
                }

                // Unload all assembly contexts
                _loader.UnloadAllExtensions();

                System.Diagnostics.Debug.WriteLine("[EXT_SERVICE] Extension service disposed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Error disposing extension service: {ex.Message}");
            }
        }

        public async Task RefreshExtensionsAsync()
        {
            await Task.CompletedTask;
            
            System.Diagnostics.Debug.WriteLine("[EXT_SERVICE] Refreshing extensions...");

            // Discover from both locations
            System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] User extensions path: {_userExtensionsPath}");
            System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Bundled extensions path: {_bundledExtensionsPath}");
            var userExtensions = _loader.DiscoverExtensions(_userExtensionsPath);
            var bundledExtensions = _loader.DiscoverExtensions(_bundledExtensionsPath);

            // Prioritize bundled extensions over user extensions
            var allExtensions = bundledExtensions.Concat(userExtensions).ToList();

            // Update registry
            foreach (var ext in allExtensions)
            {
                if (!_extensions.ContainsKey(ext.Manifest.Name))
                {
                    // Load enabled state from settings
                    ext.IsEnabled = GetSavedEnabledState(ext.Manifest.Name);
                    _extensions[ext.Manifest.Name] = ext;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Found {_extensions.Count} extensions");
        }

        private async Task<bool> RequestRuntimePermissionAsync(string extensionName, ExtensionPermission permission)
        {
            if (!_extensions.TryGetValue(extensionName, out var metadata))
            {
                return false;
            }

            var granted = await _permissionPrompt.PromptRuntimePermissionAsync(metadata, permission);
            
            if (granted)
            {
                metadata.GrantedPermissions |= permission;
                _trustStore.SetGrantedPermissions(extensionName, metadata.GrantedPermissions);
            }

            return granted;
        }

        private void SaveExtensionSettings()
        {
            try
            {
                var settings = _extensions.Values.ToDictionary(
                    e => e.Manifest.Name,
                    e => new { e.IsEnabled }
                );

                var json = JsonSerializer.Serialize(settings);
                _storage.SetProperty("extensions:settings", json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EXT_SERVICE] Failed to save settings: {ex.Message}");
            }
        }

        private bool GetSavedEnabledState(string extensionName)
        {
            try
            {
                var json = _storage.GetProperty<string>("extensions:settings");
                if (string.IsNullOrEmpty(json))
                {
                    return true; // Enabled by default
                }

                var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (settings != null && settings.TryGetValue(extensionName, out var setting))
                {
                    if (setting.TryGetProperty("IsEnabled", out var enabled))
                    {
                        return enabled.GetBoolean();
                    }
                }
            }
            catch { }

            return true; // Default to enabled
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }

    }
}

