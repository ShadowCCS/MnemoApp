using System;
using MnemoApp.Core.Common;

namespace MnemoApp.Core.Navigation
{
    public interface INavigationService
    {
        void Navigate<TViewModel>() where TViewModel : ViewModelBase;
        void Navigate(object viewModelInstance);
        ViewModelBase? CurrentViewModel { get; }
        event Action<ViewModelBase>? ViewModelChanged;
    }
}
