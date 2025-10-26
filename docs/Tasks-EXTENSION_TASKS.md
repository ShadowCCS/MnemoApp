# Extension Task System

## Overview

The MnemoApp task system allows extensions to create their own custom tasks that integrate seamlessly with the core task scheduler. Extension tasks work alongside core tasks and can be scheduled via the MnemoAPI.

## Creating Extension Tasks

### Task Structure

Extension tasks should be placed in the `Extensions/<ExtensionName>/Tasks/` folder and must inherit from `MnemoTaskBase`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using MnemoApp.Core.Tasks.Models;

namespace MyExtension.Tasks
{
    public class MyCustomTask : MnemoTaskBase
    {
        public MyCustomTask(string name, string? description = null)
            : base(name, description, TaskPriority.Normal, TaskExecutionMode.Parallel)
        {
            // Initialize your task
        }

        protected override async Task<TaskResult> ExecuteTaskAsync(
            IProgress<TaskProgress> progress, 
            CancellationToken cancellationToken)
        {
            try
            {
                // Report progress (0.0 to 1.0)
                progress.Report(new TaskProgress(0.1, "Starting..."));
                
                // Do your work here
                // Check cancellation token periodically
                cancellationToken.ThrowIfCancellationRequested();
                
                // More work...
                progress.Report(new TaskProgress(0.5, "Half way there..."));
                
                // Final work
                progress.Report(new TaskProgress(1.0, "Complete"));
                
                // Return success with optional data
                return new TaskResult(true, yourResultData);
            }
            catch (OperationCanceledException)
            {
                throw; // Let scheduler handle cancellation
            }
            catch (Exception ex)
            {
                return new TaskResult(false, ErrorMessage: ex.Message);
            }
        }
    }
}
```

### Task Execution Modes

Choose the appropriate execution mode for your task:

- **`TaskExecutionMode.Parallel`**: Can run alongside other parallel tasks (default, best for most tasks)
- **`TaskExecutionMode.Exclusive`**: Requires exclusive access (no other tasks running, use for AI tasks or resource-intensive operations)
- **`TaskExecutionMode.UIThread`**: Must run on the UI thread (use sparingly)

### Task Priority

Set priority to control execution order when multiple tasks are pending:

- `TaskPriority.Low` (0)
- `TaskPriority.Normal` (5)
- `TaskPriority.High` (10)
- `TaskPriority.Critical` (15)

## Scheduling Extension Tasks

Extension tasks are scheduled through the MnemoAPI:

```csharp
// In your extension code
var task = new MyCustomTask("Task Name", "Task Description");
Guid taskId = mnemoAPI.tasks.scheduleTask(task);

// Optional: Show toast for task
mnemoAPI.ui.toast.showForTask(taskId, showProgress: true);

// Optional: Show loading overlay
mnemoAPI.ui.loading.showForTask(taskId);
```

## Accessing Services in Extension Tasks

Extension tasks can receive services through constructor injection. Common services:

### AI Service
```csharp
public class MyAITask : MnemoTaskBase
{
    private readonly IAIService _aiService;
    private readonly IModelSelectionService _modelSelection;
    
    public MyAITask(IAIService aiService, IModelSelectionService modelSelection)
        : base("AI Task", null, TaskPriority.High, TaskExecutionMode.Exclusive, usingAI: true)
    {
        _aiService = aiService;
        _modelSelection = modelSelection;
    }
    
    protected override async Task<TaskResult> ExecuteTaskAsync(
        IProgress<TaskProgress> progress, 
        CancellationToken cancellationToken)
    {
        var request = new AIInferenceRequest
        {
            ModelName = _modelSelection.SelectedModel,
            Prompt = "Your prompt here",
            MaxTokens = 1000
        };
        
        var response = await _aiService.InferAsync(request, cancellationToken);
        return new TaskResult(response.Success, response.Response);
    }
}
```

### Storage Service
```csharp
public class MyStorageTask : MnemoTaskBase
{
    private readonly IRuntimeStorage _storage;
    
    public MyStorageTask(IRuntimeStorage storage)
        : base("Storage Task", null)
    {
        _storage = storage;
    }
    
    protected override async Task<TaskResult> ExecuteTaskAsync(
        IProgress<TaskProgress> progress, 
        CancellationToken cancellationToken)
    {
        // Store data
        _storage.SetProperty("myext:data", myData);
        
        // Retrieve data
        var data = _storage.GetProperty<MyDataType>("myext:data");
        
        return new TaskResult(true, data);
    }
}
```

## Best Practices

1. **Always check cancellation tokens** during long-running operations
2. **Report progress regularly** to keep users informed
3. **Use descriptive names and messages** for progress updates
4. **Handle exceptions gracefully** and return meaningful error messages
5. **Use appropriate execution modes** (Parallel for most tasks, Exclusive for AI/resource-intensive)
6. **Namespace storage keys** with your extension prefix (e.g., `myext:keyname`)
7. **Test cancellation behavior** to ensure tasks can be stopped cleanly
8. **Provide estimated durations** by overriding `EstimatedDuration` property

## Example: Complete Extension Task

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using MnemoApp.Core.Tasks.Models;
using MnemoApp.Data.Runtime;

namespace MyExtension.Tasks
{
    public class DataProcessingTask : MnemoTaskBase
    {
        private readonly IRuntimeStorage _storage;
        private readonly string[] _dataItems;

        public DataProcessingTask(IRuntimeStorage storage, string[] dataItems)
            : base("Process Data", $"Processing {dataItems.Length} items", TaskPriority.Normal, TaskExecutionMode.Parallel)
        {
            _storage = storage;
            _dataItems = dataItems;
        }

        public override TimeSpan? EstimatedDuration => TimeSpan.FromSeconds(_dataItems.Length * 2);

        protected override async Task<TaskResult> ExecuteTaskAsync(
            IProgress<TaskProgress> progress, 
            CancellationToken cancellationToken)
        {
            try
            {
                var results = new List<string>();
                
                for (int i = 0; i < _dataItems.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var progressValue = (double)i / _dataItems.Length;
                    progress.Report(new TaskProgress(progressValue, $"Processing item {i + 1} of {_dataItems.Length}"));
                    
                    // Simulate processing
                    await Task.Delay(2000, cancellationToken);
                    var result = $"Processed: {_dataItems[i]}";
                    results.Add(result);
                }
                
                // Store results
                _storage.SetProperty($"myext:results:{Guid.NewGuid()}", results.ToArray());
                
                progress.Report(new TaskProgress(1.0, "Processing complete"));
                return new TaskResult(true, results.ToArray());
            }
            catch (OperationCanceledException)
            {
                throw; // Let scheduler handle
            }
            catch (Exception ex)
            {
                return new TaskResult(false, ErrorMessage: $"Processing failed: {ex.Message}");
            }
        }
    }
}
```

## Task Discovery (Future Enhancement)

Currently, extension tasks are explicitly instantiated and scheduled. A future enhancement could implement automatic task discovery where extensions register task factories or provide task definitions in their manifest files.
