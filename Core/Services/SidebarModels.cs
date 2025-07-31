using System;
using System.Collections.ObjectModel;
using MnemoApp.Core.Common;

namespace MnemoApp.Core.Services
{
    public class SidebarItem : ViewModelBase
    {
        private string _title = string.Empty;
        private Type _viewModelType = null!;
        private string _iconPath = string.Empty;
        private bool _isSelected;

        public string Title 
        { 
            get => _title; 
            set => SetProperty(ref _title, value); 
        }

        public Type ViewModelType 
        { 
            get => _viewModelType; 
            set => SetProperty(ref _viewModelType, value); 
        }

        public string IconPath 
        { 
            get => _iconPath; 
            set => SetProperty(ref _iconPath, value); 
        }

        public bool IsSelected 
        { 
            get => _isSelected; 
            set => SetProperty(ref _isSelected, value); 
        }

        public SidebarItem(string title, Type viewModelType, string iconPath = "")
        {
            Title = title;
            ViewModelType = viewModelType;
            IconPath = iconPath;
        }
    }

    public class SidebarCategory : ViewModelBase
    {
        private string _name = string.Empty;
        private ObservableCollection<SidebarItem> _items = new();

        public string Name 
        { 
            get => _name; 
            set => SetProperty(ref _name, value); 
        }

        public ObservableCollection<SidebarItem> Items 
        { 
            get => _items; 
            set => SetProperty(ref _items, value); 
        }

        public SidebarCategory(string name)
        {
            Name = name;
        }
    }
}