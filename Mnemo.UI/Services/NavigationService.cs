using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.UI.Services;

public class NavigationService : INavigationService, INotifyPropertyChanged
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Type> _routes = new();
    private readonly Dictionary<string, string> _routeNames = new();
    private object? _currentViewModel;
    private readonly Stack<string> _history = new();

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        Breadcrumbs = new ObservableCollection<BreadcrumbItem>();
    }

    public object? CurrentViewModel
    {
        get => _currentViewModel;
        private set
        {
            _currentViewModel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentRoute));
            UpdateBreadcrumbs();
        }
    }

    public string? CurrentRoute => _history.Count > 0 ? _history.Peek() : null;

    public bool CanGoBack => _history.Count > 1;

    public event Action? CanGoBackChanged;

    public ObservableCollection<BreadcrumbItem> Breadcrumbs { get; }

    public void RegisterRoute(string route, Type viewModelType)
    {
        _routes[route] = viewModelType;
    }

    public void RegisterRoute(string route, Type viewModelType, string displayName)
    {
        _routes[route] = viewModelType;
        _routeNames[route] = displayName;
    }

    public void NavigateTo(string route)
    {
        if (_routes.TryGetValue(route, out var vmType))
        {
            var vm = _serviceProvider.GetRequiredService(vmType);
            _history.Push(route);
            CurrentViewModel = vm;
            CanGoBackChanged?.Invoke();
        }
    }

    public void NavigateToBreadcrumb(BreadcrumbItem item)
    {
        NavigateTo(item.Route);
    }

    private void UpdateBreadcrumbs()
    {
        Breadcrumbs.Clear();
        if (_history.Count == 0) return;

        var routes = _history.ToList();
        routes.Reverse();

        for (int i = 0; i < routes.Count; i++)
        {
            var route = routes[i];
            var name = _routeNames.TryGetValue(route, out var displayName) ? displayName : route;
            Breadcrumbs.Add(new BreadcrumbItem
            {
                Title = name,
                Route = route,
                IsLast = i == routes.Count - 1
            });
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
