using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace Mnemo.UI.Components
{
    public partial class Paginator : UserControl, System.ComponentModel.INotifyPropertyChanged
    {
        public static readonly StyledProperty<int> CurrentPageProperty =
            AvaloniaProperty.Register<Paginator, int>(nameof(CurrentPage), defaultValue: 1);

        public static readonly StyledProperty<int> TotalPagesProperty =
            AvaloniaProperty.Register<Paginator, int>(nameof(TotalPages), defaultValue: 1);

        public static readonly StyledProperty<ICommand?> PageChangedCommandProperty =
            AvaloniaProperty.Register<Paginator, ICommand?>(nameof(PageChangedCommand));

        public int CurrentPage
        {
            get => GetValue(CurrentPageProperty);
            set => SetValue(CurrentPageProperty, value);
        }

        public int TotalPages
        {
            get => GetValue(TotalPagesProperty);
            set => SetValue(TotalPagesProperty, value);
        }

        public ICommand? PageChangedCommand
        {
            get => GetValue(PageChangedCommandProperty);
            set => SetValue(PageChangedCommandProperty, value);
        }

        public ObservableCollection<int> PageNumbers { get; } = new();

        public ICommand NextPageCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand GoToPageCommand { get; }

        public bool CanGoNext => CurrentPage < TotalPages;
        public bool CanGoPrev => CurrentPage > 1;

        public static readonly PageNumberActiveConverter PageNumberActiveConverter = new();

        public Paginator()
        {
            NextPageCommand = new RelayCommand(NextPage, () => CanGoNext);
            PrevPageCommand = new RelayCommand(PrevPage, () => CanGoPrev);
            GoToPageCommand = new RelayCommand<int>(GoToPage);

            InitializeComponent();

            this.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CurrentPage) || e.PropertyName == nameof(TotalPages))
                {
                    UpdatePageNumbers();
                    OnPropertyChanged(nameof(CanGoNext));
                    OnPropertyChanged(nameof(CanGoPrev));
                }
            };

            UpdatePageNumbers();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void UpdatePageNumbers()
        {
            PageNumbers.Clear();

            if (TotalPages <= 0) return;

            // Show up to 5 page numbers
            const int maxVisible = 5;
            int start, end;

            if (TotalPages <= maxVisible)
            {
                start = 1;
                end = TotalPages;
            }
            else
            {
                // Center around current page
                int half = maxVisible / 2;
                start = Math.Max(1, CurrentPage - half);
                end = Math.Min(TotalPages, start + maxVisible - 1);

                // Adjust start if we're near the end
                if (end - start < maxVisible - 1)
                {
                    start = Math.Max(1, end - maxVisible + 1);
                }
            }

            for (int i = start; i <= end; i++)
            {
                PageNumbers.Add(i);
            }
        }

        private void NextPage()
        {
            if (CanGoNext)
            {
                CurrentPage++;
                PageChangedCommand?.Execute(CurrentPage);
            }
        }

        private void PrevPage()
        {
            if (CanGoPrev)
            {
                CurrentPage--;
                PageChangedCommand?.Execute(CurrentPage);
            }
        }

        private void GoToPage(int page)
        {
            if (page >= 1 && page <= TotalPages && page != CurrentPage)
            {
                CurrentPage = page;
                PageChangedCommand?.Execute(CurrentPage);
            }
        }

        public new event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
        
        /// <summary>
        /// Manually refreshes the button states and command availability.
        /// This is useful when the pagination state is updated externally.
        /// </summary>
        public void RefreshButtonStates()
        {
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CanGoPrev));
            
            // Notify the commands that their CanExecute state has changed
            if (NextPageCommand is RelayCommand nextCmd)
                nextCmd.NotifyCanExecuteChanged();
            if (PrevPageCommand is RelayCommand prevCmd)
                prevCmd.NotifyCanExecuteChanged();
        }
        
        /// <summary>
        /// Manually updates the page numbers collection.
        /// This is useful when the pagination state is updated externally.
        /// </summary>
        public void RefreshPageNumbers()
        {
            UpdatePageNumbers();
        }
    }

    public class PageNumberActiveConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count == 2 && values[0] is int currentPage && values[1] is int pageNumber)
            {
                // return a boolean for DataTrigger
                return currentPage == pageNumber;
            }
            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PageNumberBackgroundConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            // values: [currentPage:int, page:int, defaultBackground:IBrush?]
            if (values.Count >= 2 && values[0] is int currentPage && values[1] is int pageNumber)
            {
                var isActive = currentPage == pageNumber;
                if (isActive)
                {
                    return Application.Current?.FindResource("PaginatorActiveBrush") as IBrush ?? Brushes.Transparent;
                }
            }
            // fallback transparent/default
            if (values.Count >= 3 && values[2] is IBrush b)
                return b;
            return Brushes.Transparent;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class PageNumberForegroundConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            // values: [currentPage:int, page:int]
            if (values.Count >= 2 && values[0] is int currentPage && values[1] is int pageNumber)
            {
                var isActive = currentPage == pageNumber;
                if (isActive)
                {
                    return Brushes.White;
                }
            }
            return Application.Current?.FindResource("PaginatorTextBrush") as IBrush ?? Brushes.Black;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


