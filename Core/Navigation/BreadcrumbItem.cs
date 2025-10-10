using System;
using System.ComponentModel;
using MnemoApp.Core.Common;

namespace MnemoApp.Core.Navigation
{
    public class BreadcrumbItem : INotifyPropertyChanged
    {
        private bool _isLast = false;

        public string Title { get; set; } = string.Empty;
        public ViewModelBase? ViewModel { get; set; }
        public Action? NavigateAction { get; set; }
        
        public bool IsLast 
        { 
            get => _isLast; 
            set 
            { 
                if (_isLast != value)
                {
                    _isLast = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLast)));
                }
            } 
        }
        
        public BreadcrumbItem(string title, ViewModelBase? viewModel = null, Action? navigateAction = null)
        {
            Title = title;
            ViewModel = viewModel;
            NavigateAction = navigateAction;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

