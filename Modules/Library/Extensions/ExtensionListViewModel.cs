using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using MnemoApp.Core.Common;
using MnemoApp.Core.Extensions.Services;
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Core.Services;

namespace MnemoApp.Modules.Library.Extensions
{
    public class ExtensionListViewModel : ViewModelBase
    {
        private readonly IMnemoAPI _api;
        private readonly IExtensionService _extensionService;
        private string _searchText = string.Empty;

        public ObservableCollection<ExtensionListItem> Extensions { get; } = new();

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterExtensions();
                }
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand InstallCommand { get; }

        public ExtensionListViewModel(IMnemoAPI api, IExtensionService extensionService)
        {
            _api = api;
            _extensionService = extensionService;

            RefreshCommand = new AsyncRelayCommand(RefreshExtensionsAsync);
            InstallCommand = new AsyncRelayCommand(InstallExtensionAsync);

            // Subscribe to extension state changes
            _extensionService.ExtensionStateChanged += OnExtensionStateChanged;

            // Load initial data
            _ = LoadExtensionsAsync();
        }

        private async Task LoadExtensionsAsync()
        {
            await _extensionService.RefreshExtensionsAsync();

            var extensions = _api.extensions.listInstalled();

            // Apply current search filter
            var filteredExtensions = string.IsNullOrWhiteSpace(SearchText)
                ? extensions
                : extensions.Where(ext =>
                    ext.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    ext.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    (ext.Author?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (ext.Description?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    ext.Permissions.Any(p => p.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                ).ToList();

            Extensions.Clear();
            foreach (var ext in filteredExtensions)
            {
                Extensions.Add(new ExtensionListItem(ext, _api, _extensionService));
            }
        }

        private async Task RefreshExtensionsAsync()
        {
            await LoadExtensionsAsync();
            _api.ui.toast.show("Extensions refreshed", type: ToastType.Info);
        }

        private async Task InstallExtensionAsync()
        {
            try
            {
                // Show initial toast
                _api.ui.toast.show("Opening extension browser...", type: ToastType.Info);

                // Get the main window for file picker
                var mainWindow = Application.Current?.ApplicationLifetime is
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow : null;

                if (mainWindow == null)
                {
                    _api.ui.toast.show("Error", "Could not access main window for file picker", ToastType.Error);
                    return;
                }

                var topLevel = TopLevel.GetTopLevel(mainWindow);
                if (topLevel == null)
                {
                    _api.ui.toast.show("Error", "Could not access top-level window", ToastType.Error);
                    return;
                }

                // Open folder picker for extension directory
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Extension Directory",
                    AllowMultiple = false
                });

                var folder = folders?.FirstOrDefault();
                var extensionPath = folder?.TryGetLocalPath();

                if (string.IsNullOrWhiteSpace(extensionPath))
                {
                    _api.ui.toast.show("Cancelled", "No extension directory selected", ToastType.Info);
                    return;
                }

                // Check if it's a .mnemoext file instead
                if (System.IO.File.Exists(extensionPath) &&
                    System.IO.Path.GetExtension(extensionPath).Equals(".mnemoext", StringComparison.OrdinalIgnoreCase))
                {
                    _api.ui.toast.show("Installing", "Installing extension package...", ToastType.Info);

                // Install the .mnemoext package
                var (installSuccess, installError) = await _extensionService.InstallExtensionAsync(extensionPath);

                if (installSuccess)
                {
                    // Refresh the extension list
                    await LoadExtensionsAsync();

                    _api.ui.toast.show("Success", "Extension package installed successfully!", ToastType.Success);
                }
                else
                {
                    _api.ui.toast.show("Installation Failed", installError ?? "Unknown error occurred", ToastType.Error);
                }
                    return;
                }

                // Validate that this is an extension directory
                var manifestPath = System.IO.Path.Combine(extensionPath, "manifest.json");
                if (!System.IO.File.Exists(manifestPath))
                {
                    _api.ui.toast.show("Invalid Extension", "Selected directory does not contain a manifest.json file", ToastType.Error);
                    return;
                }

                // Show progress
                _api.ui.toast.show("Installing", "Installing extension...", ToastType.Info);

                // Install the extension
                var (success, error) = await _extensionService.InstallExtensionAsync(extensionPath);

                if (success)
                {
                    // Refresh the extension list
                    await LoadExtensionsAsync();

                    _api.ui.toast.show("Success", "Extension installed successfully!", ToastType.Success);
                }
                else
                {
                    _api.ui.toast.show("Installation Failed", error ?? "Unknown error occurred", ToastType.Error);
                }
            }
            catch (Exception ex)
            {
                _api.ui.toast.show("Error", $"Failed to install extension: {ex.Message}", ToastType.Error);
            }
        }

        private void FilterExtensions()
        {
            var extensions = _api.extensions.listInstalled();
            var filteredExtensions = string.IsNullOrWhiteSpace(SearchText)
                ? extensions
                : extensions.Where(ext =>
                    ext.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    ext.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    (ext.Author?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (ext.Description?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    ext.Permissions.Any(p => p.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                ).ToList();

            Extensions.Clear();
            foreach (var ext in filteredExtensions)
            {
                Extensions.Add(new ExtensionListItem(ext, _api, _extensionService));
            }
        }

        private void OnExtensionStateChanged(object? sender, ExtensionEventArgs e)
        {
            // Reload extensions when state changes
            _ = LoadExtensionsAsync();
        }
    }
}

