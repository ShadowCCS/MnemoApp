using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MnemoApp.UI.Components;
using MnemoApp.Core.MnemoAPI;
using System;
using System.Threading.Tasks;

namespace MnemoApp.Modules.Paths.Overlays
{
    public partial class CreatePathOverlay : UserControl
    {
        private readonly IMnemoAPI _mnemoAPI;
        
        public CreatePathOverlay(IMnemoAPI mnemoAPI)
        {
            _mnemoAPI = mnemoAPI ?? throw new System.ArgumentNullException(nameof(mnemoAPI));
            InitializeComponent();
            
            var inputBuilder = this.FindControl<InputBuilder>("InputBuilderControl");
            if (inputBuilder != null)
            {
                var vm = new InputBuilderViewModel(_mnemoAPI);
                vm.ApplyConfigurationFromControl(inputBuilder.InputMethods);
                vm.HeaderNamespace = inputBuilder.HeaderNamespace;
                vm.TitleKey = inputBuilder.TitleKey;
                vm.DescriptionKey = inputBuilder.DescriptionKey;
                vm.Generated += OnGeneratePath;
                inputBuilder.DataContext = vm;
            }
        }

        private void OnGeneratePath(string notes)
        {
            if (string.IsNullOrWhiteSpace(notes))
            {
                _mnemoAPI.ui.toast.show("Error", "No notes provided");
                return;
            }

            try
            {
                // Schedule the learning path creation task
                var taskId = _mnemoAPI.tasks.scheduleCreatePath(notes);
                
                // Show progress notifications
                _mnemoAPI.ui.toast.showForTask(taskId, showProgress: true);
                _mnemoAPI.ui.loading.showForTask(taskId);
                
                // Subscribe to task completion to show result
                _mnemoAPI.tasks.onTaskCompleted(task =>
                {
                    if (task.Id == taskId)
                    {
                        var pathData = task.Result?.Data;
                        if (pathData != null)
                        {
                            _mnemoAPI.ui.toast.show("Success!", "Learning path created successfully!");
                            System.Diagnostics.Debug.WriteLine($"[CREATE_PATH_OVERLAY] Path created: {pathData}");
                        }
                    }
                });
                
                // Subscribe to task failure
                _mnemoAPI.tasks.onTaskFailed(task =>
                {
                    if (task.Id == taskId)
                    {
                        _mnemoAPI.ui.toast.show("Error", $"{task.ErrorMessage}");
                    }
                });
                
                // Close the overlay
                _mnemoAPI.ui.overlay.CloseOverlay("CreatePathOverlay", null);
            }
            catch (Exception ex)
            {
                _mnemoAPI.ui.toast.show("Error", $"{ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[CREATE_PATH_OVERLAY] Error: {ex}");
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}


