using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using MnemoApp.Core.Storage;

namespace MnemoApp.Core.AI.Services
{
    /// <summary>
    /// Centralized service for managing model selection across the application
    /// </summary>
    public class ModelSelectionService : IModelSelectionService
    {
        private readonly IAIService _aiService;
        private readonly MnemoDataApi _dataApi;
        private string? _selectedModel;

        public ObservableCollection<string> AvailableModels { get; } = new();

        public string? SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (_selectedModel == value) return;
                _selectedModel = value;
                SelectedModelChanged?.Invoke(_selectedModel);
                
                // Persist selection
                if (!string.IsNullOrWhiteSpace(_selectedModel))
                {
                    _dataApi.SetProperty("AI.SelectedModel", _selectedModel);
                }
            }
        }

        public bool HasModels => AvailableModels.Count > 0;

        public event Action<string?>? SelectedModelChanged;

        public ModelSelectionService(IAIService aiService, MnemoDataApi dataApi)
        {
            _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
            _dataApi = dataApi ?? throw new ArgumentNullException(nameof(dataApi));

            // Subscribe to model updates from AI service
            _aiService.ModelsUpdated += OnModelsUpdated;
        }

        public async Task InitializeAsync()
        {
            await RefreshModelsAsync();
            
            // Restore saved selection
            var saved = _dataApi.GetProperty<string>("AI.SelectedModel");
            if (!string.IsNullOrWhiteSpace(saved) && AvailableModels.Contains(saved))
            {
                _selectedModel = saved;
                SelectedModelChanged?.Invoke(_selectedModel);
            }
            else if (AvailableModels.Count > 0)
            {
                SelectedModel = AvailableModels[0];
            }
        }

        public async Task RefreshModelsAsync()
        {
            await _aiService.RefreshModelsAsync();
            // Model updates will be handled by OnModelsUpdated
        }

        private void OnModelsUpdated(System.Collections.Generic.IReadOnlyList<Models.AIModel> models)
        {
            var currentSelection = SelectedModel;
            
            AvailableModels.Clear();
            foreach (var model in models)
            {
                AvailableModels.Add(model.Manifest.Name);
            }

            // Maintain selection if still valid, otherwise pick first available
            if (!string.IsNullOrWhiteSpace(currentSelection) && AvailableModels.Contains(currentSelection))
            {
                // Selection is still valid, keep it
                return;
            }

            // Either no selection or invalid selection, pick first available
            if (AvailableModels.Count > 0)
            {
                SelectedModel = AvailableModels[0];
            }
            else
            {
                SelectedModel = null;
            }
        }
    }
}
