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
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Core.Overlays;
using MnemoApp.Core.Services;

namespace MnemoApp.UI.Components.Overlays
{
    public partial class LanguageSelectOverlay : UserControl, INotifyPropertyChanged
    {
        public string OverlayName { get; set; } = "LanguageSelectOverlay";
        
        public ILocalizationService? LocalizationService { get; set; }
        public IMnemoAPI? MnemoAPI { get; set; }

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
            SelectLanguageCommand = new RelayCommand<LanguageManifest>(OnSelectLanguage);
            ConfirmCommand = new AsyncRelayCommand(ConfirmAsync);
            NextPageCommand = new RelayCommand(NextPage, CanGoNext);
            PrevPageCommand = new RelayCommand(PrevPage, CanGoPrev);
            // Hidden in XAML but define to avoid binding errors
            ImportCommand = new RelayCommand(() => { });
            ExportCommand = new RelayCommand(() => { });
            DataContext = this;
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
            var loc = LocalizationService ?? ApplicationHost.GetServiceProvider().GetRequiredService<ILocalizationService>();
            var items = await loc.GetAvailableLanguagesAsync();
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
            ((RelayCommand)NextPageCommand).RaiseCanExecuteChanged();
            ((RelayCommand)PrevPageCommand).RaiseCanExecuteChanged();
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

        private bool CanGoNext() => _currentPage + 1 < Math.Max(1, (int)Math.Ceiling((double)_filtered.Count / PageSize));
        private bool CanGoPrev() => _currentPage > 0;

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
                var loc = LocalizationService ?? ApplicationHost.GetServiceProvider().GetRequiredService<ILocalizationService>();
                var ok = await loc.SetLanguageAsync(SelectedCode!);
                if (!ok)
                {
                    var api = MnemoAPI ?? ApplicationHost.GetServiceProvider().GetRequiredService<IMnemoAPI>();
                    await api.ui.overlay.CreateDialog("Apply failed", "Failed to set language", "OK", "", null, null);
                }
            }
            catch (Exception ex)
            {
                var api = MnemoAPI ?? ApplicationHost.GetServiceProvider().GetRequiredService<IMnemoAPI>();
                await api.ui.overlay.CreateDialog("Apply failed", ex.Message, "OK", "", null, null);
            }
            finally
            {
                var overlays = ApplicationHost.GetServiceProvider().GetRequiredService<IOverlayService>();
                overlays.CloseOverlay(OverlayName, SelectedCode);
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}


