using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using MnemoApp.Core.Common;
using CommunityToolkit.Mvvm.Input;
using MnemoApp.Modules.Paths;
using MnemoApp.Modules.Dashboard;
using MnemoApp.Modules.Settings;
using MnemoApp.Modules.TestModule;
using MnemoApp.Core.Extensions;
using MnemoApp.Core.Extensions.Services;
using MnemoApp.Core.Extensions.Models;

namespace MnemoApp.Core.Navigation
{
    public class NavigationService : INavigationService
    {
        private readonly IServiceProvider _serviceProvider;
        private ViewModelBase? _currentViewModel;
        private readonly IExtensionService _extensionService;

        public NavigationService(IServiceProvider serviceProvider, IExtensionService extensionService)
        {
            _serviceProvider = serviceProvider;
            _extensionService = extensionService;
            GoBackCommand = new RelayCommand(GoBack, () => CanGoBack);
            Breadcrumbs.CollectionChanged += (s, e) =>
            {
                (GoBackCommand as RelayCommand)?.NotifyCanExecuteChanged();
                OnCanGoBackChanged();
            };
        }

        public ViewModelBase? CurrentViewModel => _currentViewModel;
        public ObservableCollection<BreadcrumbItem> Breadcrumbs { get; } = new();
        public bool CanGoBack => Breadcrumbs.Count > 1;
        public ICommand GoBackCommand { get; }

        public event Action<ViewModelBase>? ViewModelChanged;
        public event Action? CanGoBackChanged;
        
        private void OnCanGoBackChanged() => CanGoBackChanged?.Invoke();

        public void Navigate<TViewModel>() where TViewModel : ViewModelBase
        {
            ViewModelBase? vm = null;
            
            // Try to get from DI first
            vm = _serviceProvider.GetService<TViewModel>();
            
            // If not in DI, check if it's an extension ViewModel and create via extension system
            if (vm == null)
            {
                var extensionContext = FindExtensionContextForViewModel(typeof(TViewModel));
                if (extensionContext != null && extensionContext is ExtensionContext extCtx)
                {
                    // Create extension ViewModel via its service provider
                    vm = extCtx.ServiceProvider.GetService<TViewModel>();
                    
                    if (vm == null)
                    {
                        throw new InvalidOperationException($"Extension ViewModel {typeof(TViewModel).Name} is not registered in the extension's service provider");
                    }
                }
                else
                {
                    // Last resort: try to instantiate with parameterless constructor
                    try
                    {
                        var instance = Activator.CreateInstance(typeof(TViewModel));
                        vm = instance as ViewModelBase ?? throw new InvalidOperationException($"Created instance is not a ViewModelBase");
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Unable to create ViewModel {typeof(TViewModel).Name}. Not in DI and no extension context found.", ex);
                    }
                }
            }

            if (vm == null)
            {
                throw new InvalidOperationException($"Unable to resolve ViewModel {typeof(TViewModel).Name}");
            }

            // Set extension context if this is an extension ViewModel
            SetExtensionContextIfNeeded(vm);

            // Add breadcrumb for main navigation items
            var breadcrumbTitle = GetBreadcrumbTitleForViewModel<TViewModel>();
            if (!string.IsNullOrEmpty(breadcrumbTitle))
            {
                Navigate(vm, breadcrumbTitle, clearBreadcrumbs: true);
            }
            else
            {
                Navigate(vm);
            }
        }

        public void Navigate(object viewModelInstance)
        {
            if (viewModelInstance is ViewModelBase vm)
            {
                // Set extension context if this is an extension ViewModel
                SetExtensionContextIfNeeded(vm);

                _currentViewModel = vm;

                // Clear breadcrumbs when navigating to root views (no breadcrumb title provided)
                Breadcrumbs.Clear();

                ViewModelChanged?.Invoke(vm);
            }
            else
            {
                throw new ArgumentException("Navigation target must be a ViewModelBase", nameof(viewModelInstance));
            }
        }

        public void Navigate(object viewModelInstance, string breadcrumbTitle, bool clearBreadcrumbs = false)
        {
            if (viewModelInstance is ViewModelBase vm)
            {
                // Set extension context if this is an extension ViewModel
                SetExtensionContextIfNeeded(vm);

                _currentViewModel = vm;

                // Clear breadcrumbs if requested (for root navigation with breadcrumb)
                if (clearBreadcrumbs)
                {
                    Breadcrumbs.Clear();
                }

                // Add breadcrumb with back navigation action
                var breadcrumb = new BreadcrumbItem(breadcrumbTitle, vm, () => NavigateToBreadcrumb(new BreadcrumbItem(breadcrumbTitle, vm)));
                Breadcrumbs.Add(breadcrumb);

                // Update IsLast property for all breadcrumbs
                UpdateBreadcrumbIsLastProperty();

                ViewModelChanged?.Invoke(vm);
            }
            else
            {
                throw new ArgumentException("Navigation target must be a ViewModelBase", nameof(viewModelInstance));
            }
        }

        public void NavigateToBreadcrumb(BreadcrumbItem breadcrumb)
        {
            if (breadcrumb.ViewModel != null)
            {
                // Find the breadcrumb in the list
                var index = Breadcrumbs.IndexOf(breadcrumb);
                if (index >= 0)
                {
                    // Remove all breadcrumbs after this one
                    while (Breadcrumbs.Count > index + 1)
                    {
                        Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);
                    }
                }
                
                // Update IsLast property for all breadcrumbs
                UpdateBreadcrumbIsLastProperty();
                
                _currentViewModel = breadcrumb.ViewModel;
                ViewModelChanged?.Invoke(breadcrumb.ViewModel);
            }
        }

        public void GoBack()
        {
            if (CanGoBack)
            {
                // Navigate to the second-to-last breadcrumb
                var previousBreadcrumb = Breadcrumbs[Breadcrumbs.Count - 2];
                NavigateToBreadcrumb(previousBreadcrumb);
            }
        }

        /// <summary>
        /// Sets the extension context on a ViewModel if it supports extension context
        /// </summary>
        private void SetExtensionContextIfNeeded(ViewModelBase viewModel)
        {
            // Check if this ViewModel has a SetExtensionContext method
            var setContextMethod = viewModel.GetType().GetMethod("SetExtensionContext");
            if (setContextMethod != null)
            {
                // Find the extension context that corresponds to this ViewModel's extension
                var extensionContext = FindExtensionContextForViewModel(viewModel.GetType());
                if (extensionContext != null)
                {
                    setContextMethod.Invoke(viewModel, new[] { extensionContext });
                }
            }
        }

        /// <summary>
        /// Finds the extension context for a given ViewModel type
        /// </summary>
        private IExtensionContext? FindExtensionContextForViewModel(Type viewModelType)
        {
            // For bundled extensions, check if the ViewModel is in the same namespace as the extension
            var viewModelNamespace = viewModelType.Namespace;

            if (!string.IsNullOrEmpty(viewModelNamespace))
            {
                // Extract extension name from namespace (e.g., "MnemoApp.Extensions.SampleExtension" -> "SampleExtension")
                var namespaceParts = viewModelNamespace.Split('.');
                if (namespaceParts.Length >= 3 && namespaceParts[1] == "Extensions")
                {
                    var extensionName = namespaceParts[2];
                    var context = _extensionService.GetExtensionContext(extensionName);
                    if (context != null)
                    {
                        return context;
                    }
                }

                // Fallback: Check all loaded extensions
                var allExtensions = _extensionService.GetAllExtensions();
                foreach (var metadata in allExtensions.Where(e => e.State == MnemoApp.Core.Extensions.Models.ExtensionState.Enabled || e.State == MnemoApp.Core.Extensions.Models.ExtensionState.Loaded))
                {
                    var context = _extensionService.GetExtensionContext(metadata.Manifest.Name);
                    if (context != null)
                    {
                        var instance = _extensionService.GetExtensionInstance(metadata.Manifest.Name);
                        if (instance is IUIContributor)
                        {
                            // Check namespace match
                            var extensionNamespace = instance.GetType().Namespace;
                            if (!string.IsNullOrEmpty(extensionNamespace) &&
                                viewModelNamespace.StartsWith(extensionNamespace, StringComparison.OrdinalIgnoreCase))
                            {
                                return context;
                            }

                            // Check if ViewModel type name contains extension name
                            var extensionCleanName = metadata.Manifest.Name.Replace(" ", "").Replace("-", "");
                            if (viewModelType.Name.Contains(extensionCleanName, StringComparison.OrdinalIgnoreCase))
                            {
                                return context;
                            }
                        }
                    }
                }
            }

            return null;
        }

        private string GetBreadcrumbTitleForViewModel<TViewModel>() where TViewModel : ViewModelBase
        {
            var viewModelType = typeof(TViewModel);

            // Map ViewModel types to their breadcrumb titles
            // These are root navigation items that will show breadcrumb only when entering sub-navigation
            return viewModelType.Name switch
            {
                "PathsViewModel" => "Learning Paths",
                "DashboardViewModel" => "Dashboard",
                "SettingsViewModel" => "Settings",
                "TestModuleViewModel" => "Test Module",
                _ => string.Empty
            };
        }

        private void UpdateBreadcrumbIsLastProperty()
        {
            for (int i = 0; i < Breadcrumbs.Count; i++)
            {
                Breadcrumbs[i].IsLast = (i == Breadcrumbs.Count - 1);
            }
        }
    }
}
