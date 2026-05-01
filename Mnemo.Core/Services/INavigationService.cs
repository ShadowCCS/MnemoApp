using System;
using System.Collections.ObjectModel;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface INavigationService : INavigationRegistry
{
    object? CurrentViewModel { get; }
    string? CurrentRoute { get; }
    void NavigateTo(string route);
    void NavigateTo(string route, object? parameter);
    bool CanGoBack { get; }
    event Action? CanGoBackChanged;

    /// <summary>Fires after <see cref="NavigateTo"/> resolves the new view model and updates history.</summary>
    event EventHandler<NavigationChangedEventArgs>? Navigated;
    ObservableCollection<BreadcrumbItem> Breadcrumbs { get; }
    void NavigateToBreadcrumb(BreadcrumbItem item);
}

