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

namespace MnemoApp.Core.Navigation
{
    public class NavigationService : INavigationService
    {
        private readonly IServiceProvider _serviceProvider;
        private ViewModelBase? _currentViewModel;

        public NavigationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
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
            var vm = _serviceProvider.GetRequiredService<TViewModel>();
            
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
                _currentViewModel = vm;
                
                // Clear breadcrumbs if requested (for root navigation with breadcrumb)
                if (clearBreadcrumbs)
                {
                    Breadcrumbs.Clear();
                }
                
                // Add breadcrumb with back navigation action
                var breadcrumb = new BreadcrumbItem(breadcrumbTitle, vm, () => NavigateToBreadcrumb(new BreadcrumbItem(breadcrumbTitle, vm)));
                Breadcrumbs.Add(breadcrumb);
                
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
    }
}
