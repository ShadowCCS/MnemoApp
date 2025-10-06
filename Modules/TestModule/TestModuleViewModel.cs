using System;
using System.Windows.Input;
using MnemoApp.Core.Common;
using MnemoApp.Core.MnemoAPI;
using CommunityToolkit.Mvvm.Input;

namespace MnemoApp.Modules.TestModule;

public class TestModuleViewModel : ViewModelBase
{
    private readonly IMnemoAPI? _mnemoAPI;

    public TestModuleViewModel(IMnemoAPI mnemoAPI)
    {
        _mnemoAPI = mnemoAPI;
        TestAICommand = new RelayCommand(TestAI);
    }

    public ICommand TestAICommand { get; }

    private async void TestAI()
    {
        if (_mnemoAPI == null) return;
        
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
        await _mnemoAPI.ui.overlay.CreateDialog("Task Testing", result, "OK", "Cancel");
    }
}
