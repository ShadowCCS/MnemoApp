using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using MnemoApp.Core.Common;

namespace MnemoApp.Core.Navigation
{
    public interface INavigationService
    {
        void Navigate<TViewModel>() where TViewModel : ViewModelBase;
        void Navigate(object viewModelInstance);
        void Navigate(object viewModelInstance, string breadcrumbTitle, bool clearBreadcrumbs = false);
        void NavigateToBreadcrumb(BreadcrumbItem breadcrumb);
        void GoBack();
        bool CanGoBack { get; }
        ICommand GoBackCommand { get; }
        ViewModelBase? CurrentViewModel { get; }
        ObservableCollection<BreadcrumbItem> Breadcrumbs { get; }
        event Action<ViewModelBase>? ViewModelChanged;
        event Action? CanGoBackChanged;
    }
}
