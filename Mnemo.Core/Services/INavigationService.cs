using System;
using System.Collections.ObjectModel;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface INavigationService : INavigationRegistry
{
    object? CurrentViewModel { get; }
    string? CurrentRoute { get; }
    void NavigateTo(string route);
    bool CanGoBack { get; }
    event Action? CanGoBackChanged;
    ObservableCollection<BreadcrumbItem> Breadcrumbs { get; }
    void NavigateToBreadcrumb(BreadcrumbItem item);
}

