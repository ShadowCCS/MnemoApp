using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using MnemoApp.Core.Extensions;
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Core.Common;
using MnemoApp.Core.Services;
using MnemoApp.Data.Runtime;
using MnemoApp.Extensions.SampleExtension.Tasks;

namespace MnemoApp.Extensions.SampleExtension
{
    /// <summary>
    /// Sample extension demonstrating MnemoApp's extension system capabilities
    /// </summary>
    public class SampleExtension : IMnemoExtension, IUIContributor, IServiceContributor
    {
        private IExtensionContext? _context;
        private Guid? _topbarButtonId;
        private bool _uiRegistered = false;

        public async Task OnLoadAsync(IExtensionContext context)
        {
            try
            {
                _context = context;
                context.Logger.LogInfo("SampleExtension loading...");

                // Store some initial data
                context.API.data.SetProperty($"{context.StoragePrefix}loadCount", 
                    context.API.data.GetProperty<int>($"{context.StoragePrefix}loadCount", 0) + 1);
                
                context.Logger.LogInfo($"SampleExtension loaded {context.API.data.GetProperty<int>($"{context.StoragePrefix}loadCount")} times");
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error in OnLoadAsync: {ex.Message}");
                context.Logger.LogError($"Stack trace: {ex.StackTrace}");
                throw;
            }

            await Task.CompletedTask;
        }

        public async Task OnEnableAsync()
        {
            if (_context == null) return;

            _context.Logger.LogInfo("SampleExtension enabling...");

            // Show welcome toast
            _context.API.ui.toast.show("Sample Extension", "Extension enabled successfully!", ToastType.Success);

            await Task.CompletedTask;
        }

        public async Task OnDisableAsync()
        {
            if (_context == null) return;

            _context.Logger.LogInfo("SampleExtension disabling...");

            // Remove topbar button
            if (_topbarButtonId.HasValue)
            {
                _context.API.ui.topbar.remove(_topbarButtonId.Value);
                _topbarButtonId = null;
            }

            // Unregister sidebar item
            _context.API.sidebar.Unregister("Sample Extension", "Extensions");

            // Reset UI registration flag
            _uiRegistered = false;

            await Task.CompletedTask;
        }

        public async Task OnUnloadAsync()
        {
            if (_context == null) return;

            _context.Logger.LogInfo("SampleExtension unloading...");

            // Clean up resources
            _context = null;

            await Task.CompletedTask;
        }

        public async Task RegisterUIAsync(IExtensionContext context)
        {
            try
            {
                context.Logger.LogInfo("SampleExtension RegisterUIAsync called");
                
                if (_uiRegistered) 
                {
                    context.Logger.LogInfo("UI already registered, skipping");
                    return;
                }

                context.Logger.LogInfo("Registering sidebar item...");
                // Register sidebar item with custom navigation
                context.API.sidebar.Register(
                    "Sample Extension",
                    typeof(SampleExtensionViewModel),
                    "Extensions",
                    "avares://MnemoApp/Modules/Extensions/SampleExtension/icon.svg"
                );

                context.Logger.LogInfo("Adding topbar button...");
                // Add topbar button
                _topbarButtonId = context.API.ui.topbar.addButton(
                    "avares://MnemoApp/Modules/Extensions/SampleExtension/icon.svg",
                    command: new CommunityToolkit.Mvvm.Input.RelayCommand(() => ShowSampleOverlay(context)),
                    toolTip: "Open Sample Extension",
                    order: 100
                );

                context.Logger.LogInfo($"Topbar button added with ID: {_topbarButtonId}");

                // Store context for later use when ViewModel is instantiated
                _context = context;
                _uiRegistered = true;

                context.Logger.LogInfo("UI registration completed successfully");
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error in RegisterUIAsync: {ex.Message}");
                context.Logger.LogError($"Stack trace: {ex.StackTrace}");
                throw;
            }

            await Task.CompletedTask;
        }

        public void RegisterServices(IServiceCollection services)
        {
            // Register the view model
            services.AddTransient<SampleExtensionViewModel>();
        }

        private void ShowSampleOverlay(IExtensionContext context)
        {
            var overlay = new SampleOverlayControl(context);
            context.API.ui.overlay.Show<object>(overlay);
        }
    }

    /// <summary>
    /// Sample overlay control demonstrating overlay capabilities
    /// </summary>
    public class SampleOverlayControl : UserControl
    {
        private readonly IExtensionContext _context;

        public SampleOverlayControl(IExtensionContext context)
        {
            _context = context;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 10
            };

            var title = new TextBlock
            {
                Text = "Sample Extension Overlay",
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var description = new TextBlock
            {
                Text = "This overlay demonstrates the extension system's overlay capabilities.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var button = new Button
            {
                Content = "Schedule Sample Task",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Command = new CommunityToolkit.Mvvm.Input.RelayCommand(ScheduleSampleTask)
            };

            var closeButton = new Button
            {
                Content = "Close",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Command = new CommunityToolkit.Mvvm.Input.RelayCommand(CloseOverlay)
            };

            panel.Children.Add(title);
            panel.Children.Add(description);
            panel.Children.Add(button);
            panel.Children.Add(closeButton);

            Content = panel;
        }

        private void ScheduleSampleTask()
        {
            var task = new SampleTask(_context.Services.GetRequiredService<IRuntimeStorage>(), 
                $"Sample task created at {DateTime.Now:HH:mm:ss}");
            
            var taskId = _context.API.tasks.scheduleTask(task);
            _context.API.ui.toast.showForTask(taskId, showProgress: true);
        }

        private void CloseOverlay()
        {
            _context.API.ui.overlay.CloseAllOverlays();
        }
    }
}
