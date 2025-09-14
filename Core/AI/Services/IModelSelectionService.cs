using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace MnemoApp.Core.AI.Services
{
    /// <summary>
    /// Service for managing model selection state across the application
    /// </summary>
    public interface IModelSelectionService
    {
        /// <summary>
        /// Observable collection of available model names
        /// </summary>
        ObservableCollection<string> AvailableModels { get; }

        /// <summary>
        /// Currently selected model name
        /// </summary>
        string? SelectedModel { get; set; }

        /// <summary>
        /// Event fired when the selected model changes
        /// </summary>
        event Action<string?>? SelectedModelChanged;

        /// <summary>
        /// Initialize the service
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Refresh the available models list
        /// </summary>
        Task RefreshModelsAsync();

        /// <summary>
        /// Check if the service has any models available
        /// </summary>
        bool HasModels { get; }
    }
}
