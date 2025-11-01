using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using MnemoApp.Core.Common;
using MnemoApp.Core.MnemoAPI;
using MnemoApp.Data.Runtime;
using MnemoApp.Modules.Notes.Models;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MnemoApp.Modules.TestModule;

public partial class TestModuleViewModel : ViewModelBase
{
    private readonly IMnemoAPI? _mnemoAPI;
    private readonly IRuntimeStorage? _storage;

    [ObservableProperty]
    private string _pathTitle = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ManualUnitViewModel> _units = new();

    public TestModuleViewModel(IMnemoAPI mnemoAPI, IRuntimeStorage storage)
    {
        _mnemoAPI = mnemoAPI;
        _storage = storage;
        TestAICommand = new RelayCommand(TestAI);
        AddUnitCommand = new RelayCommand(AddUnit);
        SavePathCommand = new RelayCommand(SavePath);
        WipeNotesCommand = new RelayCommand(WipeNotes);
    }

    public ICommand TestAICommand { get; }
    public ICommand AddUnitCommand { get; }
    public ICommand SavePathCommand { get; }
    public ICommand WipeNotesCommand { get; }

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

    private void AddUnit()
    {
        var order = Units.Count + 1;
        var unit = new ManualUnitViewModel
        {
            Order = order,
            Title = $"Unit {order}",
            Content = string.Empty
        };
        unit.RemoveRequested += (s, e) => Units.Remove(unit);
        Units.Add(unit);
    }

    private void SavePath()
    {
        if (_storage == null || _mnemoAPI == null) return;

        if (string.IsNullOrWhiteSpace(PathTitle))
        {
            _mnemoAPI.ui.toast.show("Error", "Please enter a path title");
            return;
        }

        if (Units.Count == 0)
        {
            _mnemoAPI.ui.toast.show("Error", "Please add at least one unit");
            return;
        }

        try
        {
            var pathId = Guid.NewGuid().ToString();
            var pathData = new PathData
            {
                Id = pathId,
                Title = PathTitle,
                CreatedAt = DateTime.UtcNow,
                Units = Units.Select((u, idx) => new UnitStructure
                {
                    Order = idx + 1,
                    Title = u.Title,
                    Content = u.Content,
                    Notes = string.Empty
                }).ToArray()
            };

            // Store in unified Content table
            var pathKey = $"Content/Paths/{pathData.Id}";
            _storage.SetProperty(pathKey, pathData);

            // Maintain a global list of all path IDs
            var listKey = "Content/Paths/list";
            var existing = _storage.GetProperty<string[]>(listKey) ?? Array.Empty<string>();
            if (Array.IndexOf(existing, pathData.Id) < 0)
            {
                var updated = new string[existing.Length + 1];
                Array.Copy(existing, updated, existing.Length);
                updated[existing.Length] = pathData.Id;
                _storage.SetProperty(listKey, updated);
            }

            _mnemoAPI.ui.toast.show("Success", $"Created path: {PathTitle}");
            
            // Clear form
            PathTitle = string.Empty;
            Units.Clear();
        }
        catch (Exception ex)
        {
            _mnemoAPI.ui.toast.show("Error", $"Failed to save path: {ex.Message}");
        }
    }

    private void WipeNotes()
    {
        if (_storage == null || _mnemoAPI == null) return;

        try
        {
            // Get all notes using ListContent
            var notes = _storage.ListContent<NoteData>("Notes");
            int count = 0;

            // Delete each note
            foreach (var noteItem in notes)
            {
                _storage.RemoveProperty($"Content/Notes/{noteItem.ContentId}");
                count++;
            }

            _mnemoAPI.ui.toast.show("Success", $"Deleted {count} note(s)");
        }
        catch (Exception ex)
        {
            _mnemoAPI.ui.toast.show("Error", $"Failed to wipe notes: {ex.Message}");
        }
    }
}

public partial class ManualUnitViewModel : ObservableObject
{
    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _content = string.Empty;

    public event EventHandler? RemoveRequested;

    public ICommand RemoveCommand { get; }

    public ManualUnitViewModel()
    {
        RemoveCommand = new RelayCommand(() => RemoveRequested?.Invoke(this, EventArgs.Empty));
    }
}

#region Data Models (copied from CreateLearningPathTask for compatibility)

public class UnitStructure
{
    public int Order { get; set; }
    public string? Title { get; set; }
    public string? Notes { get; set; }
    public string? Content { get; set; }
}

public class PathData
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public UnitStructure[]? Units { get; set; }
}

#endregion
