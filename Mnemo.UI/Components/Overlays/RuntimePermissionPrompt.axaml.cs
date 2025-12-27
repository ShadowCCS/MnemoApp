using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls;
using Mnemo.Core.Models;
using CommunityToolkit.Mvvm.Input;

namespace Mnemo.UI.Components.Overlays
{
    public partial class RuntimePermissionPrompt : UserControl, INotifyPropertyChanged
    {
        private readonly ExtensionMetadata? _metadata;
        private readonly ExtensionPermission _permission;
        private bool _hasResponded = false;

        public event Action<bool>? OnPermissionResult;

        public string ExtensionInfo => _metadata != null ? $"{_metadata.Manifest.DisplayName ?? _metadata.Manifest.Name} requests additional access" : string.Empty;
        
        public string PermissionName => GetPermissionDisplayName(_permission);
        
        public string PermissionDescription => GetPermissionDescription(_permission);

        public ICommand AllowCommand { get; }
        public ICommand DenyCommand { get; }

        public RuntimePermissionPrompt()
        {
            _permission = ExtensionPermission.None;
            AllowCommand = new RelayCommand(AllowPermission);
            DenyCommand = new RelayCommand(DenyPermission);
            InitializeComponent();
        }

        public RuntimePermissionPrompt(ExtensionMetadata metadata, ExtensionPermission permission) : this()
        {
            _metadata = metadata;
            _permission = permission;
            
            // Notify that properties have changed
            OnPropertyChanged(nameof(ExtensionInfo));
            OnPropertyChanged(nameof(PermissionName));
            OnPropertyChanged(nameof(PermissionDescription));
        }

        private string GetPermissionDisplayName(ExtensionPermission permission)
        {
            return permission switch
            {
                ExtensionPermission.FileAccess => "File Access",
                ExtensionPermission.NetworkAccess => "Network Access",
                ExtensionPermission.UIAccess => "UI Access",
                ExtensionPermission.ApiRegistration => "API Registration",
                ExtensionPermission.FullTrust => "Full Trust",
                _ => permission.ToString()
            };
        }

        private string GetPermissionDescription(ExtensionPermission permission)
        {
            return permission switch
            {
                ExtensionPermission.FileAccess => "This extension wants to read and write files outside its own directory.",
                ExtensionPermission.NetworkAccess => "This extension wants to make HTTP requests and access network resources.",
                ExtensionPermission.UIAccess => "This extension wants to add UI elements like sidebar items and overlays.",
                ExtensionPermission.ApiRegistration => "This extension wants to register custom API endpoints.",
                ExtensionPermission.FullTrust => "This extension requests complete access to all application APIs.",
                _ => "This extension requests additional permissions."
            };
        }

        private void AllowPermission()
        {
            if (_hasResponded) return;
            _hasResponded = true;

            // Notify the result
            OnPermissionResult?.Invoke(true);
        }

        private void DenyPermission()
        {
            if (_hasResponded) return;
            _hasResponded = true;

            // Notify the result
            OnPermissionResult?.Invoke(false);
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

