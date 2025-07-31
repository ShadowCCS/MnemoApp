using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MnemoApp.Core.Common;
using MnemoApp.Core.Navigation;
namespace MnemoApp.Core.Shell
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public ViewModelBase? CurrentPage => _navigationService?.CurrentViewModel;

        private readonly INavigationService? _navigationService;
        
        private bool _isSidebarCollapsed = false;

        public bool IsSidebarCollapsed
        {
            get => _isSidebarCollapsed;
            set => SetProperty(ref _isSidebarCollapsed, value);
        }

        public ICommand ToggleSidebarCommand { get; }

        // Parameterless constructor for design-time
        public MainWindowViewModel()
        {
            _navigationService = null;
            ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
        }
        
        public MainWindowViewModel(INavigationService navigationService)
        {
            _navigationService = navigationService;
            _navigationService.ViewModelChanged += OnViewModelChanged;
            ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
        }

        private void OnViewModelChanged(ViewModelBase vm)
        {
            OnPropertyChanged(nameof(CurrentPage));
        }

        public void ToggleSidebar()
        {
            IsSidebarCollapsed = !IsSidebarCollapsed;
        }
    }
}
