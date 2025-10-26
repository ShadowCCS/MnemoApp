using System;
using System.Linq;
using MnemoApp.Core.Extensions.Models;

namespace MnemoApp.Core.Extensions.Security
{
    /// <summary>
    /// Validates extension permissions
    /// </summary>
    public class PermissionValidator
    {
        /// <summary>
        /// Check if an extension has a specific permission
        /// </summary>
        public bool HasPermission(ExtensionMetadata metadata, ExtensionPermission permission)
        {
            // FullTrust grants everything
            if (metadata.GrantedPermissions.HasFlag(ExtensionPermission.FullTrust))
            {
                return true;
            }

            // Development extensions get full trust by default
            if (metadata.TrustLevel == ExtensionTrustLevel.Development)
            {
                return true;
            }

            return metadata.GrantedPermissions.HasFlag(permission);
        }

        /// <summary>
        /// Validate that granted permissions are a subset of requested permissions
        /// </summary>
        public bool ValidateGrantedPermissions(ExtensionMetadata metadata)
        {
            var requested = metadata.Manifest.GetPermissionFlags();
            var granted = metadata.GrantedPermissions;

            // FullTrust can be granted even if not requested (for trusted extensions)
            if (granted.HasFlag(ExtensionPermission.FullTrust))
            {
                return true;
            }

            // Granted permissions should not exceed requested permissions
            return (granted & ~requested) == 0;
        }

        /// <summary>
        /// Get permission display name
        /// </summary>
        public string GetPermissionDisplayName(ExtensionPermission permission)
        {
            return permission switch
            {
                ExtensionPermission.FileAccess => "File Access",
                ExtensionPermission.NetworkAccess => "Network Access",
                ExtensionPermission.UIAccess => "User Interface Access",
                ExtensionPermission.ApiRegistration => "API Registration",
                ExtensionPermission.FullTrust => "Full Trust",
                _ => permission.ToString()
            };
        }

        /// <summary>
        /// Get permission description
        /// </summary>
        public string GetPermissionDescription(ExtensionPermission permission)
        {
            return permission switch
            {
                ExtensionPermission.FileAccess => "Read and write files outside the extension directory",
                ExtensionPermission.NetworkAccess => "Make network requests to external services",
                ExtensionPermission.UIAccess => "Register views, sidebar items, and overlays",
                ExtensionPermission.ApiRegistration => "Register custom API endpoints",
                ExtensionPermission.FullTrust => "Unrestricted access to all application features",
                _ => "Unknown permission"
            };
        }

        /// <summary>
        /// Get all individual permissions from a flags enum
        /// </summary>
        public ExtensionPermission[] GetIndividualPermissions(ExtensionPermission flags)
        {
            return Enum.GetValues<ExtensionPermission>()
                .Where(p => p != ExtensionPermission.None && p != ExtensionPermission.All)
                .Where(p => flags.HasFlag(p))
                .ToArray();
        }
    }
}

