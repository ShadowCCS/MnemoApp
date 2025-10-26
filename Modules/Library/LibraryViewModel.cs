using MnemoApp.Core.Common;
using MnemoApp.Modules.Library.Extensions;
using System.Windows.Input;
using Avalonia.Data.Converters;
using System;
using System.Globalization;
using Avalonia.Controls;

namespace MnemoApp.Modules.Library
{
    public class LibraryViewModel : ViewModelBase
    {
        private int _selectedTabIndex = 0;
        private object? _currentContent;

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        public object? CurrentContent
        {
            get => _currentContent;
            set => SetProperty(ref _currentContent, value);
        }

        public ICommand SelectExtensionsCommand { get; }
        public ICommand SelectContentCommand { get; }

        public static IValueConverter ExtensionsTabConverter { get; } = new TabIndexConverter(0);
        public static IValueConverter ContentTabConverter { get; } = new TabIndexConverter(1);

        public LibraryViewModel()
        {
            SelectExtensionsCommand = new RelayCommand(() => SelectTab(0));
            SelectContentCommand = new RelayCommand(() => SelectTab(1));
            
            // Initialize with Extensions tab
            SelectTab(0);
        }

        private void SelectTab(int tabIndex)
        {
            SelectedTabIndex = tabIndex;
            
            switch (tabIndex)
            {
                case 0:
                    CurrentContent = new ExtensionListView();
                    break;
                case 1:
                    CurrentContent = CreateContentPlaceholder();
                    break;
            }
        }

        private object CreateContentPlaceholder()
        {
            return new TextBlock
            {
                Text = "Content management coming soon...",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = Avalonia.Media.Brushes.Gray
            };
        }
    }

    public class TabIndexConverter : IValueConverter
    {
        private readonly int _targetIndex;

        public TabIndexConverter(int targetIndex)
        {
            _targetIndex = targetIndex;
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int selectedIndex)
            {
                return selectedIndex == _targetIndex;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        public void Execute(object? parameter)
        {
            _execute();
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

