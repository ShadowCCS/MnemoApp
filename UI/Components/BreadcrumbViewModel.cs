using System;
using MnemoApp.Core.Common;
using MnemoApp.Core.Navigation;

namespace MnemoApp.UI.Components
{
    public class BreadcrumbViewModel : ViewModelBase, IDisposable
    {
        private readonly INavigationService _navigationService;
        private bool _disposed;

        public BreadcrumbViewModel(INavigationService navigationService)
        {
            _navigationService = navigationService;
            _navigationService.CanGoBackChanged += OnCanGoBackChanged;
        }

        public INavigationService NavigationService => _navigationService;

        public bool IsVisible => _navigationService.CanGoBack;

        private void OnCanGoBackChanged()
        {
            OnPropertyChanged(nameof(IsVisible));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _navigationService.CanGoBackChanged -= OnCanGoBackChanged;
                _disposed = true;
            }
        }
    }
}

