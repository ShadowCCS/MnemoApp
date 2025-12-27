using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;
using System.Linq;

namespace Mnemo.UI.Components.Sidebar;

public class SidebarViewModel : ViewModelBase
{
    private readonly ISidebarService _sidebarService;
    private readonly INavigationService _navigationService;

    public ObservableCollection<SidebarCategory> Categories => _sidebarService.Categories;

    public bool IsSidebarCollapsed
    {
        get => _sidebarService.IsCollapsed;
        set => _sidebarService.IsCollapsed = value;
    }

    public ICommand ToggleSidebarCommand { get; }
    public ICommand NavigateCommand { get; }

    public SidebarViewModel(ISidebarService sidebarService, INavigationService navigationService)
    {
        _sidebarService = sidebarService;
        _navigationService = navigationService;

        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);
        NavigateCommand = new RelayCommand<SidebarItem>(NavigateToItem);

        if (_sidebarService is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ISidebarService.IsCollapsed))
                {
                    OnPropertyChanged(nameof(IsSidebarCollapsed));
                }
            };
        }

        if (_navigationService is INotifyPropertyChanged navNpc)
        {
            navNpc.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(INavigationService.CurrentViewModel))
                {
                    UpdateSelection();
                }
            };
        }
        
        // Set initial selection
        UpdateSelection();
    }

    private void NavigateToItem(SidebarItem? item)
    {
        if (item != null)
        {
            _navigationService.NavigateTo(item.Route);
        }
    }

    private void UpdateSelection()
    {
        var currentRoute = _navigationService.CurrentRoute;
        
        foreach (var category in Categories)
        {
            foreach (var item in category.Items)
            {
                item.IsSelected = item.Route == currentRoute;
            }
        }
    }
}

