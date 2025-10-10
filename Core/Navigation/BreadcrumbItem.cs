using System;
using MnemoApp.Core.Common;

namespace MnemoApp.Core.Navigation
{
    public class BreadcrumbItem
    {
        public string Title { get; set; } = string.Empty;
        public ViewModelBase? ViewModel { get; set; }
        public Action? NavigateAction { get; set; }
        
        public BreadcrumbItem(string title, ViewModelBase? viewModel = null, Action? navigateAction = null)
        {
            Title = title;
            ViewModel = viewModel;
            NavigateAction = navigateAction;
        }
    }
}

