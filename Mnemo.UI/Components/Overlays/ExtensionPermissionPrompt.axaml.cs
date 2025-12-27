using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Controls;
using Mnemo.Core.Models;
using CommunityToolkit.Mvvm.Input;

namespace Mnemo.UI.Components.Overlays
{
    public partial class ExtensionPermissionPrompt : UserControl, INotifyPropertyChanged
    {
        private readonly ExtensionMetadata? _metadata;
        private readonly ExtensionPermission _requestedPermissions;
        private ExtensionPermission _grantedPermissions;
        private bool _hasResponded = false;

        public event Action<bool, ExtensionPermission>? OnPermissionResult;

        public ExtensionPermission GrantedPermissions => _grantedPermissions;

        public string ExtensionInfo => _metadata != null ? $"{_metadata.Manifest.DisplayName ?? _metadata.Manifest.Name} v{_metadata.Manifest.Version} by {_metadata.Manifest.Author ?? "Unknown"}" : string.Empty;
        
        public string Description => _metadata?.Manifest.Description ?? "This extension requests the following permissions:";

        public ObservableCollection<PermissionItem> PermissionItems { get; } = new();

        public ICommand ApproveCommand { get; }
        public ICommand DenyCommand { get; }

        public ExtensionPermissionPrompt()
        {
            _requestedPermissions = ExtensionPermission.None;
            _grantedPermissions = ExtensionPermission.None;
            ApproveCommand = new RelayCommand(ApprovePermissions);
            DenyCommand = new RelayCommand(DenyPermissions);
            InitializeComponent();
        }

        public ExtensionPermissionPrompt(ExtensionMetadata metadata, ExtensionPermission requestedPermissions) : this()
        {
            _metadata = metadata;
            _requestedPermissions = requestedPermissions;

            InitializePermissionItems();
            
            // Notify that properties have changed
            OnPropertyChanged(nameof(ExtensionInfo));
            OnPropertyChanged(nameof(Description));
        }

        private void InitializePermissionItems()
        {
            var permissions = new[]
            {
                (ExtensionPermission.FileAccess, "File Access", "Read and write files outside the extension's directory"),
                (ExtensionPermission.NetworkAccess, "Network Access", "Make HTTP requests and access network resources"),
                (ExtensionPermission.UIAccess, "UI Access", "Register UI elements, sidebar items, and overlays"),
                (ExtensionPermission.ApiRegistration, "API Registration", "Register custom API endpoints"),
                (ExtensionPermission.FullTrust, "Full Trust", "Complete access to all application APIs (development extensions only)")
            };

            foreach (var (permission, name, description) in permissions)
            {
                if (_requestedPermissions.HasFlag(permission))
                {
                    PermissionItems.Add(new PermissionItem
                    {
                        Permission = permission,
                        Name = name,
                        Description = description,
                        IsGranted = true // Default to checked
                    });
                }
            }
        }

        private void ApprovePermissions()
        {
            if (_hasResponded) return;
            _hasResponded = true;

            // Collect granted permissions
            _grantedPermissions = ExtensionPermission.None;

            foreach (var item in PermissionItems)
            {
                if (item.IsGranted)
                {
                    _grantedPermissions |= item.Permission;
                }
            }

            // Notify the result
            OnPermissionResult?.Invoke(true, _grantedPermissions);
        }

        private void DenyPermissions()
        {
            if (_hasResponded) return;
            _hasResponded = true;

            _grantedPermissions = ExtensionPermission.None;

            // Notify the result
            OnPermissionResult?.Invoke(false, _grantedPermissions);
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class PermissionItem
    {
        public ExtensionPermission Permission { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsGranted { get; set; }
    }
}

