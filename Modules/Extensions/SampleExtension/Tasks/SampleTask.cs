using System;
using System.Threading;
using System.Threading.Tasks;
using MnemoApp.Core.Tasks.Models;
using MnemoApp.Data.Runtime;

namespace MnemoApp.Extensions.SampleExtension.Tasks
{
    /// <summary>
    /// Example extension task demonstrating how extensions can create custom tasks
    /// </summary>
    public class SampleTask : MnemoTaskBase
    {
        private readonly IRuntimeStorage _storage;
        private readonly string _inputData;

        public SampleTask(IRuntimeStorage storage, string inputData)
            : base("Sample Extension Task", "Demonstrates extension task capabilities", TaskPriority.Normal, TaskExecutionMode.Parallel)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _inputData = inputData ?? throw new ArgumentNullException(nameof(inputData));
        }

        public override TimeSpan? EstimatedDuration => TimeSpan.FromSeconds(5);

        protected override async Task<TaskResult> ExecuteTaskAsync(IProgress<TaskProgress> progress, CancellationToken cancellationToken)
        {
            try
            {
                progress.Report(new TaskProgress(0.1, "Starting sample task..."));
                await Task.Delay(1000, cancellationToken);

                progress.Report(new TaskProgress(0.3, "Processing input data..."));
                var processedData = $"Processed: {_inputData}";
                await Task.Delay(1500, cancellationToken);

                progress.Report(new TaskProgress(0.6, "Storing results..."));
                // Store in extension-namespaced storage
                var resultKey = $"sample_ext:result:{Guid.NewGuid()}";
                _storage.SetProperty(resultKey, processedData);
                await Task.Delay(1000, cancellationToken);

                progress.Report(new TaskProgress(0.9, "Finalizing..."));
                await Task.Delay(500, cancellationToken);

                progress.Report(new TaskProgress(1.0, "Task complete"));
                
                return new TaskResult(true, new { 
                    Result = processedData, 
                    StorageKey = resultKey,
                    Timestamp = DateTime.UtcNow 
                });
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("[SAMPLE_TASK] Task cancelled");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SAMPLE_TASK] Task failed: {ex.Message}");
                return new TaskResult(false, ErrorMessage: ex.Message);
            }
        }
    }
}
