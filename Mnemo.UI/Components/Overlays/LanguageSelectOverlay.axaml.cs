using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core;
using Mnemo.Core.Services;
using Mnemo.Core.Services;
using Mnemo.Core.Services;

namespace Mnemo.UI.Components.Overlays
{
    public partial class LanguageSelectOverlay : UserControl, INotifyPropertyChanged
    {
        public string OverlayName { get; set; } = "LanguageSelectOverlay";
        
        private ILocalizationService LocService => ((App)Application.Current!).Services!.GetRequiredService<ILocalizationService>();
        private IOverlayService OverlayService => ((App)Application.Current!).Services!.GetRequiredService<IOverlayService>();

        public ObservableCollection<LanguageManifest> Languages { get; } = new();
        public ObservableCollection<LanguageManifest> PagedLanguages { get; } = new();

        private string? _selectedCode;
        public string? SelectedCode
        {
            get => _selectedCode;
            set { 
                if (_selectedCode != value) { 
                    _selectedCode = value; 
                    OnPropertyChanged(nameof(SelectedCode)); 
                } 
            }
        }

        public ICommand SelectLanguageCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand PageChangedCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ExportCommand { get; }
        
        private Paginator? _paginator;

        private string? _searchQuery;
        public string? SearchQuery
        {
            get => _searchQuery;
            set { _searchQuery = value; OnPropertyChanged(nameof(SearchQuery)); UpdateFiltered(); }
        }

        public int CurrentPageDisplay => _currentPage + 1;
        public int TotalPagesDisplay => Math.Max(1, (int)Math.Ceiling((double)_filtered.Count / PageSize));

        public LanguageSelectOverlay()
        {
            InitializeComponent();
            SelectLanguageCommand = new RelayCommand<LanguageManifest>(OnSelectLanguage);
            ConfirmCommand = new AsyncRelayCommand(ConfirmAsync);
            PageChangedCommand = new RelayCommand<int>(OnPageChanged);
            // Hidden in XAML but define to avoid binding errors
            ImportCommand = new RelayCommand(() => { });
            ExportCommand = new RelayCommand(() => { });
            DataContext = this;
            this.AttachedToVisualTree += async (_, __) => await LoadAsync();
            this.Loaded += OnLoaded;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
        
        private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Find the Paginator control
            _paginator = this.FindControl<Paginator>("PaginatorControl");
            if (_paginator != null)
            {
                UpdatePaginator();
            }
        }

        private readonly List<LanguageManifest> _all = new();
        private readonly List<LanguageManifest> _filtered = new();
        private const int PageSize = 2; // Temporarily reduced for testing
        private int _currentPage = 0;

        private async Task LoadAsync()
        {
            var items = await LocService.GetAvailableLanguagesAsync();
            Languages.Clear();
            foreach (var m in items)
            {
                // Debug: Log the IconPath to see if it's being set correctly
                System.Diagnostics.Debug.WriteLine($"Language: {m.Name}, Code: {m.Code}, IconPath: {m.IconPath}");
                Languages.Add(m);
            }
            _all.Clear();
            _all.AddRange(Languages);
            UpdateFiltered();
            SelectedCode = LocService.CurrentLanguage;
        }

        private void UpdateFiltered()
        {
            _filtered.Clear();
            IEnumerable<LanguageManifest> s = _all;
            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                var q = _searchQuery.Trim();
                s = s.Where(t => (t.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                               || (t.Code?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
            }
            _filtered.AddRange(s);
            _currentPage = 0;
            
            
            Repage();
        }

        private void Repage()
        {
            PagedLanguages.Clear();
            foreach (var t in _filtered.Skip(_currentPage * PageSize).Take(PageSize))
                PagedLanguages.Add(t);
            OnPropertyChanged(nameof(CurrentPageDisplay));
            OnPropertyChanged(nameof(TotalPagesDisplay));
            
            // Explicitly update the Paginator if it exists
            UpdatePaginator();
        }
        
        private void UpdatePaginator()
        {
            if (_paginator != null)
            {
                _paginator.CurrentPage = CurrentPageDisplay;
                _paginator.TotalPages = TotalPagesDisplay;
                
                // Manually trigger property change notifications for the calculated properties
                _paginator.RefreshButtonStates();
                _paginator.RefreshPageNumbers();
            }
        }

        private void OnPageChanged(int page)
        {
            _currentPage = page - 1; // Convert from 1-based to 0-based
            Repage();
        }

        private void OnSelectLanguage(LanguageManifest? manifest)
        {
            if (manifest == null) return;
            SelectedCode = manifest.Code;
        }

        private async Task ConfirmAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedCode)) return;
            try
            {
                var ok = await LocService.SetLanguageAsync(SelectedCode!);
                if (!ok)
                {
                    await OverlayService.CreateDialog("Apply failed", "Failed to set language", "OK", "", null, null);
                }
            }
            catch (Exception ex)
            {
                await OverlayService.CreateDialog("Apply failed", ex.Message, "OK", "", null, null);
            }
            finally
            {
                OverlayService.CloseOverlay(OverlayName, SelectedCode);
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}



