using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MnemoApp.Core.Common;
using MnemoApp.Core.Navigation;
using MnemoApp.Core.Services;

namespace MnemoApp.UI.Components.Sidebar
{
    public class SidebarViewModel : ViewModelBase, IDisposable
    {
        private readonly ISidebarService _sidebarService;
        private readonly INavigationService _navigationService;
        private bool _isSidebarCollapsed;
        private bool _disposed = false;

        public ObservableCollection<SidebarCategory> Categories => _sidebarService.Categories;

        public bool IsSidebarCollapsed
        {
            get => _isSidebarCollapsed;
            set => SetProperty(ref _isSidebarCollapsed, value);
        }

        public ICommand ToggleSidebarCommand { get; }
        public ICommand NavigateCommand { get; }

        public SidebarViewModel(ISidebarService sidebarService, INavigationService navigationService)
        {
            _sidebarService = sidebarService ?? throw new ArgumentNullException(nameof(sidebarService));
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

            ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
            NavigateCommand = new RelayCommand<SidebarItem>(NavigateToItem);

            // Subscribe to navigation changes to update selected item
            _navigationService.ViewModelChanged += OnViewModelChanged;

            // Initialize selection based on current view model at startup
            if (_navigationService.CurrentViewModel is not null)
            {
                OnViewModelChanged(_navigationService.CurrentViewModel);
            }
        }

        private void ToggleSidebar()
        {
            IsSidebarCollapsed = !IsSidebarCollapsed;
        }

        private void NavigateToItem(SidebarItem? item)
        {
            if (item?.ViewModelType != null)
            {
                // Clear previous selections
                foreach (var category in Categories)
                {
                    foreach (var categoryItem in category.Items)
                    {
                        categoryItem.IsSelected = false;
                    }
                }

                // Set current item as selected
                item.IsSelected = true;

                // Navigate using the navigation service
                try
                {
                    var navigateMethod = typeof(INavigationService).GetMethod("Navigate", new Type[0]);
                    var genericMethod = navigateMethod?.MakeGenericMethod(item.ViewModelType);
                    genericMethod?.Invoke(_navigationService, null);
                }
                catch (Exception ex)
                {
                    // Log error or handle navigation failure
                    Console.WriteLine($"Navigation failed: {ex.Message}");
                }
            }
        }

        private void OnViewModelChanged(ViewModelBase viewModel)
        {
            if (_disposed) return;
            
            // Update selection based on current view model
            foreach (var category in Categories)
            {
                foreach (var item in category.Items)
                {
                    item.IsSelected = item.ViewModelType == viewModel.GetType();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Unsubscribe from events to prevent memory leaks
                _navigationService.ViewModelChanged -= OnViewModelChanged;
                _disposed = true;
            }
        }
    }
}