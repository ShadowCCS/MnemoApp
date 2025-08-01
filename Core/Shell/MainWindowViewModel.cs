using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MnemoApp.Core.Common;
using MnemoApp.Core.Navigation;
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Core.Overlays;
using MnemoApp.UI.Components.Sidebar;
using MnemoApp.Modules.Dashboard;

namespace MnemoApp.Core.Shell
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public ViewModelBase? CurrentPage => _mnemoAPI?.navigate?.CurrentViewModel;
        public SidebarViewModel? SidebarViewModel { get; }
        public ObservableCollection<OverlayViewModelWrapper> Overlays => _mnemoAPI?.overlay?.Overlays ?? new ObservableCollection<OverlayViewModelWrapper>();

        private readonly IMnemoAPI? _mnemoAPI;

        public ICommand ToggleSidebarCommand { get; }

        // Parameterless constructor for design-time
        public MainWindowViewModel()
        {
            _mnemoAPI = null;
            SidebarViewModel = null;
            ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
        }
        
        public MainWindowViewModel(IMnemoAPI mnemoAPI, SidebarViewModel sidebarViewModel)
        {
            _mnemoAPI = mnemoAPI;
            SidebarViewModel = sidebarViewModel;
            _mnemoAPI.navigate.ViewModelChanged += OnViewModelChanged;
            ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
            
            // Navigate to default page (Dashboard)
            NavigateToDefaultPage();
        }

        private void NavigateToDefaultPage()
        {
            if (_mnemoAPI?.navigate != null)
            {
                _mnemoAPI.navigate.Navigate<DashboardViewModel>();
            }
        }

        private void OnViewModelChanged(ViewModelBase vm)
        {
            OnPropertyChanged(nameof(CurrentPage));
        }

        public void ToggleSidebar()
        {
            if (SidebarViewModel != null)
                SidebarViewModel.IsSidebarCollapsed = !SidebarViewModel.IsSidebarCollapsed;
        }
    }
}
