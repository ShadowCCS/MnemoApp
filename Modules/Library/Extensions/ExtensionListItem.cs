using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MnemoApp.Core.Common;
using MnemoApp.Core.Extensions.Services;
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Core.Services;

namespace MnemoApp.Modules.Library.Extensions
{
    public class ExtensionListItem : ViewModelBase
    {
        private readonly IMnemoAPI _api;
        private readonly IExtensionService _extensionService;
        private bool _isEnabled;
        private bool _isWorking;

        public string Name { get; }
        public string DisplayName { get; }
        public string Version { get; }
        public string? Author { get; }
        public string? Description { get; }
        public string State { get; }
        public bool HasErrors { get; }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (SetProperty(ref _isEnabled, value))
                {
                    _ = ToggleEnabledAsync();
                }
            }
        }

        public bool IsWorking
        {
            get => _isWorking;
            set => SetProperty(ref _isWorking, value);
        }

        public ICommand UninstallCommand { get; }
        public ICommand ReloadCommand { get; }
        public ICommand ViewDetailsCommand { get; }

        public ExtensionListItem(ExtensionInfo info, IMnemoAPI api, IExtensionService extensionService)
        {
            _api = api;
            _extensionService = extensionService;

            Name = info.Name;
            DisplayName = info.DisplayName;
            Version = info.Version;
            Author = info.Author;
            Description = info.Description;
            State = info.State;
            HasErrors = info.HasErrors;
            _isEnabled = info.IsEnabled;

            UninstallCommand = new AsyncRelayCommand(UninstallAsync);
            ReloadCommand = new AsyncRelayCommand(ReloadAsync);
            ViewDetailsCommand = new RelayCommand(ViewDetails);
        }

        private async System.Threading.Tasks.Task ToggleEnabledAsync()
        {
            if (IsWorking) return;

            IsWorking = true;
            try
            {
                if (_isEnabled)
                {
                    await _api.extensions.enable(Name);
                    _api.ui.toast.show($"Enabled {DisplayName}", type: ToastType.Success);
                }
                else
                {
                    await _api.extensions.disable(Name);
                    _api.ui.toast.show($"Disabled {DisplayName}", type: ToastType.Info);
                }
            }
            catch (Exception ex)
            {
                _api.ui.toast.show($"Failed to toggle extension: {ex.Message}", type: ToastType.Error);
                // Revert the toggle
                _isEnabled = !_isEnabled;
                OnPropertyChanged(nameof(IsEnabled));
            }
            finally
            {
                IsWorking = false;
            }
        }

        private async System.Threading.Tasks.Task UninstallAsync()
        {
            if (IsWorking) return;

            IsWorking = true;
            try
            {
                var success = await _api.extensions.uninstall(Name);
                if (success)
                {
                    _api.ui.toast.show($"Uninstalled {DisplayName}", type: ToastType.Success);
                }
                else
                {
                    _api.ui.toast.show($"Failed to uninstall {DisplayName}", type: ToastType.Error);
                }
            }
            catch (Exception ex)
            {
                _api.ui.toast.show($"Error: {ex.Message}", type: ToastType.Error);
            }
            finally
            {
                IsWorking = false;
            }
        }

        private async System.Threading.Tasks.Task ReloadAsync()
        {
            if (IsWorking) return;

            IsWorking = true;
            try
            {
                await _api.extensions.reload(Name);
                _api.ui.toast.show($"Reloaded {DisplayName}", type: ToastType.Success);
            }
            catch (Exception ex)
            {
                _api.ui.toast.show($"Failed to reload: {ex.Message}", type: ToastType.Error);
            }
            finally
            {
                IsWorking = false;
            }
        }

        private void ViewDetails()
        {
            // TODO: Show details overlay
            _api.ui.toast.show("Extension details coming soon...", type: ToastType.Info);
        }
    }
}

