using System;
using Microsoft.Extensions.DependencyInjection;
using MnemoApp.Core.Common;

namespace MnemoApp.Core.Navigation
{
    public class NavigationService : INavigationService
    {
        private readonly IServiceProvider _serviceProvider;
        private ViewModelBase? _currentViewModel;

        public NavigationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ViewModelBase? CurrentViewModel => _currentViewModel;

        public event Action<ViewModelBase>? ViewModelChanged;

        public void Navigate<TViewModel>() where TViewModel : ViewModelBase
        {
            var vm = _serviceProvider.GetRequiredService<TViewModel>();
            Navigate(vm);
        }

        public void Navigate(object viewModelInstance)
        {
            if (viewModelInstance is ViewModelBase vm)
            {
                _currentViewModel = vm;
                ViewModelChanged?.Invoke(vm);
            }
            else
            {
                throw new ArgumentException("Navigation target must be a ViewModelBase", nameof(viewModelInstance));
            }
        }
    }
}
