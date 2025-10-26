using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using MnemoApp.Core.Extensions.Models;
using MnemoApp.Core.Overlays;
using MnemoApp.UI.Components.Overlays;

namespace MnemoApp.Core.Extensions.Security
{
    /// <summary>
    /// Service for prompting users about extension permissions
    /// </summary>
    public class PermissionPromptService
    {
        private readonly IOverlayService _overlayService;
        private readonly PermissionValidator _validator;

        public PermissionPromptService(IOverlayService overlayService)
        {
            _overlayService = overlayService;
            _validator = new PermissionValidator();
        }

        /// <summary>
        /// Prompt user to grant permissions for an extension
        /// Returns the permissions that user granted
        /// </summary>
        public async Task<(bool approved, ExtensionPermission granted)> PromptPermissionsAsync(
            ExtensionMetadata metadata)
        {
            var requested = metadata.Manifest.GetPermissionFlags();

            // If no permissions requested, auto-approve
            if (requested == ExtensionPermission.None)
            {
                return (true, ExtensionPermission.None);
            }

            // For development extensions, grant full trust automatically
            if (metadata.TrustLevel == ExtensionTrustLevel.Development)
            {
                return (true, ExtensionPermission.FullTrust);
            }

            // Show permission prompt overlay - ensure UI creation happens on UI thread
            var tcs = new TaskCompletionSource<(bool, ExtensionPermission)>();
            
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var prompt = new MnemoApp.UI.Components.Overlays.ExtensionPermissionPrompt(metadata, requested);
                
                // Create overlay on UI thread and get the overlay ID
                var (overlayId, _) = _overlayService.CreateOverlayWithTask<bool>(prompt);
                
                prompt.OnPermissionResult += (approved, permissions) =>
                {
                    tcs.SetResult((approved, permissions));
                    _overlayService.CloseOverlay(overlayId, approved);
                };
            });

            var permissionResult = await tcs.Task;

            if (permissionResult.Item1)
            {
                // User approved - return the permissions they granted
                var granted = permissionResult.Item2;
                System.Diagnostics.Debug.WriteLine($"[PERMISSION_PROMPT] Extension '{metadata.Manifest.Name}' granted permissions: {granted}");
                return (true, granted);
            }
            else
            {
                // User denied or cancelled
                System.Diagnostics.Debug.WriteLine($"[PERMISSION_PROMPT] Extension '{metadata.Manifest.Name}' permission denied");
                return (false, ExtensionPermission.None);
            }
        }

        /// <summary>
        /// Prompt user to grant additional permission at runtime
        /// </summary>
        public async Task<bool> PromptRuntimePermissionAsync(
            ExtensionMetadata metadata,
            ExtensionPermission permission)
        {
            // Check if already granted
            if (metadata.GrantedPermissions.HasFlag(permission))
            {
                return true;
            }

            // Show runtime permission prompt overlay - ensure UI creation happens on UI thread
            var tcs = new TaskCompletionSource<bool>();
            
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var prompt = new MnemoApp.UI.Components.Overlays.RuntimePermissionPrompt(metadata, permission);
                
                // Create overlay on UI thread and get the overlay ID
                var (overlayId, _) = _overlayService.CreateOverlayWithTask<bool>(prompt);
                
                prompt.OnPermissionResult += (granted) =>
                {
                    tcs.SetResult(granted);
                    _overlayService.CloseOverlay(overlayId, granted);
                };
            });

            var result = await tcs.Task;

            System.Diagnostics.Debug.WriteLine($"[PERMISSION_PROMPT] Extension '{metadata.Manifest.Name}' runtime permission '{permission}' {(result ? "granted" : "denied")}");

            return result;
        }
    }
}

