using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using MnemoApp.Core;
using MnemoApp.Core.Overlays;
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Core.Services;

namespace MnemoApp.UI.Components.Overlays
{
    public partial class ThemeSelectOverlay : UserControl, System.ComponentModel.INotifyPropertyChanged
    {
        public string OverlayName { get; set; } = "ThemeSelectOverlay";
        
        public IMnemoAPI? MnemoAPI { get; set; }
        
        private string _selectedTheme = "Dawn";
        public string SelectedTheme
        {
            get => _selectedTheme;
            set { if (_selectedTheme != value) { _selectedTheme = value; OnPropertyChanged(nameof(SelectedTheme)); } }
        }
        public ICommand ConfirmCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand SelectThemeCommand { get; }
        public ICommand PageChangedCommand { get; }
        
        private Paginator? _paginator;

        private string? _searchQuery;
        public string? SearchQuery
        {
            get => _searchQuery;
            set { _searchQuery = value; OnPropertyChanged(nameof(SearchQuery)); UpdateFiltered(); }
        }

        public ObservableCollection<ThemeManifest> PagedThemes { get; } = new();
        
        public int CurrentPageDisplay => _currentPage + 1;
        public int TotalPagesDisplay => Math.Max(1, (int)Math.Ceiling((double)_filtered.Count / PageSize));

        public ThemeSelectOverlay()
        {
            InitializeComponent();
            ConfirmCommand = new RelayCommand(ConfirmSelection);
            ImportCommand = new AsyncRelayCommand(ImportAsync);
            ExportCommand = new AsyncRelayCommand(ExportAsync);
            SelectThemeCommand = new RelayCommand<ThemeManifest>(OnSelectTheme);
            PageChangedCommand = new RelayCommand<int>(OnPageChanged);
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
        public async void ConfirmSelection()
        {
            try
            {
                var api = MnemoAPI ?? ApplicationHost.GetServiceProvider().GetRequiredService<IMnemoAPI>();
                await api.ui.themes.applyTheme(SelectedTheme);
            }
            catch (Exception ex)
            {
                var api = MnemoAPI ?? ApplicationHost.GetServiceProvider().GetRequiredService<IMnemoAPI>();
                await api.ui.overlay.CreateDialog("Apply failed", ex.Message, "OK", "", null, null);
            }
            finally
            {
                //var overlays = ApplicationHost.GetServiceProvider().GetRequiredService<IOverlayService>();
                //overlays.CloseOverlay(OverlayName, SelectedTheme);
            }
        }

        private readonly List<ThemeManifest> _all = new();
        private readonly List<ThemeManifest> _filtered = new();
        private const int PageSize = 2; // 2x4
        private int _currentPage = 0;
        

        private async Task LoadAsync()
        {
            var api = MnemoAPI ?? ApplicationHost.GetServiceProvider().GetRequiredService<IMnemoAPI>();
            var themes = await api.ui.themes.getAllThemes();
            var current = api.ui.themes.getCurrentTheme();
            if (current != null) SelectedTheme = current.Name;
            _all.Clear();
            _all.AddRange(themes);
            api.ui.themes.startWatching();
            UpdateFiltered();
        }

        private void UpdateFiltered()
        {
            _filtered.Clear();
            IEnumerable<ThemeManifest> s = _all;
            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                var q = _searchQuery.Trim();
                s = s.Where(t => t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                 t.Author.Contains(q, StringComparison.OrdinalIgnoreCase));
            }
            _filtered.AddRange(s.OrderByDescending(t => t.LastUsed.GetValueOrDefault(System.DateTimeOffset.MinValue)));
            _currentPage = 0;
            
            
            Repage();
        }

        private void Repage()
        {
            PagedThemes.Clear();
            foreach (var t in _filtered.Skip(_currentPage * PageSize).Take(PageSize))
                PagedThemes.Add(t);
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

        private void OnSelectTheme(ThemeManifest? manifest)
        {
            if (manifest == null) return;
            SelectedTheme = manifest.Name;
        }

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            try
            {
                var api = MnemoAPI ?? ApplicationHost.GetServiceProvider().GetRequiredService<IMnemoAPI>();
                api.ui.themes.stopWatching();
            }
            catch { }
        }

        private async Task ImportAsync()
        {
            var api = MnemoAPI ?? ApplicationHost.GetServiceProvider().GetRequiredService<IMnemoAPI>();
            var topLevel = TopLevel.GetTopLevel(this);
            var folders = await topLevel!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Import Theme Folder",
                AllowMultiple = false
            });
            var folder = folders?.FirstOrDefault();
            var path = folder?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var _ = await api.ui.themes.import(path);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                await api.ui.overlay.CreateDialog("Import failed", ex.Message, "OK", "", null, null);
            }
        }

        private async Task ExportAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedTheme)) return;
            var api = MnemoAPI ?? ApplicationHost.GetServiceProvider().GetRequiredService<IMnemoAPI>();
            var topLevel = TopLevel.GetTopLevel(this);
            var folders = await topLevel!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Export Theme To",
                AllowMultiple = false
            });
            var folder = folders?.FirstOrDefault();
            var path = folder?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                await api.ui.themes.export(SelectedTheme, path);
            }
            catch (Exception ex)
            {
                await api.ui.overlay.CreateDialog("Export failed", ex.Message, "OK", "", null, null);
            }
        }

        private async Task RefreshAsync()
        {
            var api = MnemoAPI ?? ApplicationHost.GetServiceProvider().GetRequiredService<IMnemoAPI>();
            var themes = await api.ui.themes.getAllThemes();
            _all.Clear();
            _all.AddRange(themes);
            UpdateFiltered();
        }

        public new event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}


