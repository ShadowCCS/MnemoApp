using MnemoApp.Core.Common;
using MnemoApp.Core.Navigation;
namespace MnemoApp.Core.Shell
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public ViewModelBase? CurrentPage => _navigationService.CurrentViewModel;

        private readonly INavigationService _navigationService;

        public MainWindowViewModel(INavigationService navigationService)
        {
            _navigationService = navigationService;
            _navigationService.ViewModelChanged += OnViewModelChanged;
        }

        private void OnViewModelChanged(ViewModelBase vm)
        {
            OnPropertyChanged(nameof(CurrentPage));
        }
    }
}
