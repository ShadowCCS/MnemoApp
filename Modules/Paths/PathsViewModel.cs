using MnemoApp.Core.Common;
using System.Windows.Input;
using MnemoApp.Core.MnemoAPI;
using CommunityToolkit.Mvvm.Input;

namespace MnemoApp.Modules.Paths;

public class PathsViewModel : ViewModelBase
{
    private readonly IMnemoAPI _mnemoAPI;
    
        public PathsViewModel(IMnemoAPI mnemoAPI)
    {
        // This is a mock implementation for testing
        CreatePathCommand = new RelayCommand(CreatePath);
        TestAICommand = new RelayCommand(TestAI);
        _mnemoAPI = mnemoAPI;
    }

    public ICommand CreatePathCommand { get; }
    public ICommand TestAICommand { get; }
    private void CreatePath()
    {
        // Create the overlay with dependencies
        var overlay = new Modules.Paths.Overlays.CreatePathOverlay(_mnemoAPI);
        _mnemoAPI.ui.overlay.Show<string?>(overlay, name: "CreatePathOverlay");
    }
    private async void TestAI()
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<string>();
        
        var taskId = _mnemoAPI.tasks.scheduleAIGeneration(
            prompt: "Write a short story about a cat",
            name: "Task Testing",
            description: "Testing Task System",
            modelName: "default",   // optional
            maxTokens: 200          // optional
        );

        _mnemoAPI.ui.toast.showForTask(taskId, showProgress: true);
        _mnemoAPI.ui.loading.showForTask(taskId);
    
        // Subscribe to task completion
        _mnemoAPI.tasks.onTaskCompleted(task =>
        {
            if (task.Id == taskId)
            {
                var aiResult = task.Result?.Data as string ?? "No response received";
                tcs.TrySetResult(aiResult);
            }
        });

        var result = await tcs.Task;
        _mnemoAPI.ui.overlay.CreateDialog("Task Testing", result, "OK", "Cancel");
    }
}