using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MnemoApp.Core.AI.Models;

namespace MnemoApp.Core.AI.Drivers
{
    /// <summary>
    /// Interface for model-specific execution drivers
    /// </summary>
    public interface IModelDriver : IDisposable
    {
        /// <summary>
        /// Name of the driver (matches customDriver in capabilities.json)
        /// </summary>
        string DriverName { get; }

        /// <summary>
        /// Check if this driver can handle the given model
        /// </summary>
        bool CanHandle(AIModel model);

        /// <summary>
        /// Initialize the driver for a specific model
        /// </summary>
        Task<bool> InitializeAsync(AIModel model);

        /// <summary>
        /// Perform inference with the model
        /// </summary>
        Task<AIInferenceResponse> InferAsync(AIModel model, AIInferenceRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Perform streaming inference with the model
        /// </summary>
        IAsyncEnumerable<string> InferStreamAsync(AIModel model, AIInferenceRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Shutdown/cleanup the driver for a model
        /// </summary>
        Task ShutdownAsync(AIModel model);

        /// <summary>
        /// Check if the model is currently loaded and ready
        /// </summary>
        Task<bool> IsReadyAsync(AIModel model);
    }
}
