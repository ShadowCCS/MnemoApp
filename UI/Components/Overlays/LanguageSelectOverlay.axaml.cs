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
using MnemoApp.Core;
using MnemoApp.Core.Overlays;
using MnemoApp.Core.Services;

namespace MnemoApp.UI.Components.Overlays
{
    public partial class LanguageSelectOverlay : UserControl, INotifyPropertyChanged
    {
        public string OverlayName { get; set; } = "LanguageSelectOverlay";

        public ObservableCollection<LanguageManifest> Languages { get; } = new();
        public ObservableCollection<LanguageManifest> PagedLanguages { get; } = new();

        private string? _selectedCode;
        public string? SelectedCode
        {
            get => _selectedCode;
            set { if (_selectedCode != value) { _selectedCode = value; OnPropertyChanged(nameof(SelectedCode)); } }
        }

        public ICommand SelectLanguageCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ExportCommand { get; }

        private string? _searchQuery;
        public string? SearchQuery
        {
            get => _searchQuery;
            set { _searchQuery = value; OnPropertyChanged(nameof(SearchQuery)); UpdateFiltered(); }
        }

        public string PageInfo => $"{_currentPage + 1}/{Math.Max(1, (int)System.Math.Ceiling((double)_filtered.Count / PageSize))}";

        public LanguageSelectOverlay()
        {
            InitializeComponent();
            DataContext = this;
            SelectLanguageCommand = new RelayCommand<LanguageManifest>(OnSelectLanguage);
            ConfirmCommand = new AsyncRelayCommand(ConfirmAsync);
            NextPageCommand = new RelayCommand(NextPage);
            PrevPageCommand = new RelayCommand(PrevPage);
            // Hidden in XAML but define to avoid binding errors
            ImportCommand = new RelayCommand(() => { });
            ExportCommand = new RelayCommand(() => { });
            this.AttachedToVisualTree += async (_, __) => await LoadAsync();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private readonly List<LanguageManifest> _all = new();
        private readonly List<LanguageManifest> _filtered = new();
        private const int PageSize = 8;
        private int _currentPage = 0;

        private async Task LoadAsync()
        {
            var loc = ApplicationHost.Services.GetRequiredService<ILocalizationService>();
            var items = await loc.GetAvailableLanguagesAsync();
            Languages.Clear();
            foreach (var m in items)
                Languages.Add(m);
            _all.Clear();
            _all.AddRange(Languages);
            UpdateFiltered();
            SelectedCode = loc.CurrentLanguage;
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
            OnPropertyChanged(nameof(PageInfo));
        }

        private void NextPage()
        {
            var pages = (int)System.Math.Ceiling((double)_filtered.Count / PageSize);
            if (_currentPage + 1 < pages) { _currentPage++; Repage(); }
        }

        private void PrevPage()
        {
            if (_currentPage > 0) { _currentPage--; Repage(); }
        }

        private void OnSelectLanguage(LanguageManifest? manifest)
        {
            if (manifest == null) return;
            SelectedCode = manifest.Code;
        }

        private async Task ConfirmAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedCode)) return;
            var loc = ApplicationHost.Services.GetRequiredService<ILocalizationService>();
            var ok = await loc.SetLanguageAsync(SelectedCode!);
            var overlays = ApplicationHost.Services.GetService<IOverlayService>();
            overlays?.CloseOverlay(OverlayName, ok ? SelectedCode : null);
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}


