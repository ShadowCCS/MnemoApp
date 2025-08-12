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
        public ICommand NextPageCommand { get; }
        public ICommand PrevPageCommand { get; }

        private string? _searchQuery;
        public string? SearchQuery
        {
            get => _searchQuery;
            set { _searchQuery = value; OnPropertyChanged(nameof(SearchQuery)); UpdateFiltered(); }
        }

        public ObservableCollection<ThemeManifest> PagedThemes { get; } = new();
        public string PageInfo => $"{_currentPage + 1}/{Math.Max(1, (int)Math.Ceiling((double)_filtered.Count / PageSize))}";

        public ThemeSelectOverlay()
        {
            InitializeComponent();
            ConfirmCommand = new RelayCommand(ConfirmSelection);
            ImportCommand = new AsyncRelayCommand(ImportAsync);
            ExportCommand = new AsyncRelayCommand(ExportAsync);
            SelectThemeCommand = new RelayCommand<ThemeManifest>(OnSelectTheme);
            NextPageCommand = new RelayCommand(NextPage);
            PrevPageCommand = new RelayCommand(PrevPage);
            DataContext = this;
            this.AttachedToVisualTree += async (_, __) => await LoadAsync();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
        public async void ConfirmSelection()
        {
            try
            {
                var api = ApplicationHost.Services.GetRequiredService<IMnemoAPI>();
                await api.ui.themes.applyTheme(SelectedTheme);
            }
            catch (Exception ex)
            {
                var api = ApplicationHost.Services.GetRequiredService<IMnemoAPI>();
                await api.ui.overlay.CreateDialog("Apply failed", ex.Message, "OK", "", null, null);
            }
            finally
            {
                //var overlays = ApplicationHost.Services.GetRequiredService<IOverlayService>();
                //overlays.CloseOverlay(OverlayName, SelectedTheme);
            }
        }

        private readonly List<ThemeManifest> _all = new();
        private readonly List<ThemeManifest> _filtered = new();
        private const int PageSize = 8; // 2x4
        private int _currentPage = 0;
        

        private async Task LoadAsync()
        {
            var api = ApplicationHost.Services.GetRequiredService<IMnemoAPI>();
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
            OnPropertyChanged(nameof(PageInfo));
        }

        private void NextPage()
        {
            var pages = (int)Math.Ceiling((double)_filtered.Count / PageSize);
            if (_currentPage + 1 < pages) { _currentPage++; Repage(); }
        }

        private void PrevPage()
        {
            if (_currentPage > 0) { _currentPage--; Repage(); }
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
                var api = ApplicationHost.Services.GetRequiredService<IMnemoAPI>();
                api.ui.themes.stopWatching();
            }
            catch { }
        }

        private async Task ImportAsync()
        {
            var api = ApplicationHost.Services.GetRequiredService<IMnemoAPI>();
            var dlg = new OpenFolderDialog { Title = "Import Theme Folder" };
            var path = await dlg.ShowAsync(GetWindow());
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
            var api = ApplicationHost.Services.GetRequiredService<IMnemoAPI>();
            var dlg = new OpenFolderDialog { Title = "Export Theme To" };
            var path = await dlg.ShowAsync(GetWindow());
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
            var api = ApplicationHost.Services.GetRequiredService<IMnemoAPI>();
            var themes = await api.ui.themes.getAllThemes();
            _all.Clear();
            _all.AddRange(themes);
            UpdateFiltered();
        }

        private Window GetWindow()
        {
            return (Window)TopLevel.GetTopLevel(this)!;
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }
}


