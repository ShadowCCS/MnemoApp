using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MnemoApp.Core.Common;
using MnemoApp.Core.AI.Services;
using MnemoApp.Core.MnemoAPI;

namespace MnemoApp.UI.Components
{
    public partial class InputBuilderViewModel : ViewModelBase, IDisposable
    {
        private readonly IMnemoAPI _mnemoAPI;
        private Action<string?>? _modelChangedHandler;
        private CancellationTokenSource? _textMetricsCts;
        
        // Header localization (configurable by parent control)
        [ObservableProperty]
        private string headerNamespace = string.Empty; // e.g. "Paths" or "InputBuilder"

        [ObservableProperty]
        private string titleKey = string.Empty; // e.g. "Title"

        [ObservableProperty]
        private string descriptionKey = string.Empty; // e.g. "InputBuilderDescription"

        [ObservableProperty]
        private int currentTokenCount = 0;
        
        [ObservableProperty]
        private int maxTokens = 0;

        [ObservableProperty]
        private bool showTextTab = true;
        
        [ObservableProperty]
        private bool showFilesTab = true;
        
        [ObservableProperty]
        private bool showLinksTab = true;
        
        [ObservableProperty]
        private bool isTextTabActive = true;
        
        [ObservableProperty]
        private bool isFilesTabActive = false;
        
        [ObservableProperty]
        private bool isLinksTabActive = false;
        
        [ObservableProperty]
        private string textContent = string.Empty;
        
        [ObservableProperty]
        private string newLinkUrl = string.Empty;
        
        [ObservableProperty]
        private int characterCount = 0;
        
        [ObservableProperty]
        private int wordCount = 0;
        
        [ObservableProperty]
        private int percentComplete = 65;
        
        [ObservableProperty]
        private int filesProcessedCount = 987;
        
        [ObservableProperty]
        private int linksScrapedCount = 834;
        
        [ObservableProperty]
        private string totalSourcesText = "Content from 3 sources combined";

        [ObservableProperty]
        private int reservedContext = 1800; // Need to reserve x tokens for the output of the model, this is just a guess for now, we will need to see how big the outputs are. Might dynamically change it depending on the max tokens we allow during call.

        // Badge counts
        [ObservableProperty]
        private int textEntryCount = 0;
        
        public ObservableCollection<FileItemViewModel> Files { get; } = new();
        public ObservableCollection<LinkItemViewModel> Links { get; } = new();
        
        public ICommand SelectTextTabCommand { get; }
        public ICommand SelectFilesTabCommand { get; }
        public ICommand SelectLinksTabCommand { get; }
        public ICommand AddLinkCommand { get; }
        public ICommand GenerateCommand { get; }
        
        public InputBuilderViewModel(IMnemoAPI mnemoAPI)
        {
            _mnemoAPI = mnemoAPI ?? throw new ArgumentNullException(nameof(mnemoAPI));
            
            SelectTextTabCommand = new RelayCommand(SelectTextTab);
            SelectFilesTabCommand = new RelayCommand(SelectFilesTab);
            SelectLinksTabCommand = new RelayCommand(SelectLinksTab);
            AddLinkCommand = new RelayCommand(AddLink, CanAddLink);
            GenerateCommand = new RelayCommand(Generate, CanGenerate);
            
            // Initialize with default tab selection based on visibility
            EnsureValidTabSelection();
            
            // Listen to text content changes for token count updates
            PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName == nameof(TextContent))
                {
                    try
                    {
                        await DebounceUpdateTextMetricsAsync();
                        UpdateBadgeCounts();
                    }
                    catch { /* ignore */ }
                }
                if (e.PropertyName == nameof(NewLinkUrl))
                {
                    ((RelayCommand)AddLinkCommand).NotifyCanExecuteChanged();
                }
            };

            Files.CollectionChanged += (_, __) => UpdateBadgeCounts();
            Links.CollectionChanged += (_, __) => UpdateBadgeCounts();
            
            // Subscribe to model selection changes with stored handler for cleanup
            _modelChangedHandler = async _ =>
            {
                try { await UpdateModelContextAsync(); }
                catch { /* ignore */ }
            };
            _mnemoAPI.ai.SubscribeToSelectedModelChanges(_modelChangedHandler);
            
            // Initialize model context without Task.Run; rely on method-level try/catch
            _ = UpdateModelContextAsync();
        }

        public void Dispose()
        {
            try
            {
                if (_modelChangedHandler != null)
                {
                    _mnemoAPI.ai.UnsubscribeFromSelectedModelChanges(_modelChangedHandler);
                    _modelChangedHandler = null;
                }
            }
            catch { /* ignore */ }
            finally
            {
                _textMetricsCts?.Cancel();
                _textMetricsCts?.Dispose();
                _textMetricsCts = null;
            }
            GC.SuppressFinalize(this);
        }

        public void ApplyConfigurationFromControl(string inputMethodsCsv)
        {
            // Configure tabs by input methods list
            var methods = (inputMethodsCsv ?? string.Empty)
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
                .Select(s => s.ToLowerInvariant())
                .ToHashSet();

            var showText = methods.Count == 0 || methods.Contains("text");
            var showFiles = methods.Count == 0 || methods.Contains("files");
            var showLinks = methods.Count == 0 || methods.Contains("links");
            ConfigureTabs(showText, showFiles, showLinks);
        }
        
        public void ConfigureTabs(bool showText = true, bool showFiles = true, bool showLinks = true)
        {
            ShowTextTab = showText;
            ShowFilesTab = showFiles;
            ShowLinksTab = showLinks;
            
            EnsureValidTabSelection();
        }
        
        private void EnsureValidTabSelection()
        {
            // If current active tab is hidden, select the first visible tab
            if ((IsTextTabActive && !ShowTextTab) ||
                (IsFilesTabActive && !ShowFilesTab) ||
                (IsLinksTabActive && !ShowLinksTab))
            {
                if (ShowTextTab)
                    SetActiveTab(TabKind.Text);
                else if (ShowFilesTab)
                    SetActiveTab(TabKind.Files);
                else if (ShowLinksTab)
                    SetActiveTab(TabKind.Links);
            }
        }
        
        private void SelectTextTab()
        {
            if (!ShowTextTab) return;
            SetActiveTab(TabKind.Text);
        }
        
        private void SelectFilesTab()
        {
            if (!ShowFilesTab) return;
            SetActiveTab(TabKind.Files);
        }
        
        private void SelectLinksTab()
        {
            if (!ShowLinksTab) return;
            SetActiveTab(TabKind.Links);
        }

        private void SetActiveTab(TabKind tab)
        {
            IsTextTabActive = tab == TabKind.Text;
            IsFilesTabActive = tab == TabKind.Files;
            IsLinksTabActive = tab == TabKind.Links;
        }
        
        private void AddLink()
        {
            if (string.IsNullOrWhiteSpace(NewLinkUrl)) return;
            
            var linkItem = new LinkItemViewModel(NewLinkUrl);
            linkItem.RemoveCommand = new RelayCommand(() => Links.Remove(linkItem));
            Links.Add(linkItem);
            
            NewLinkUrl = string.Empty;
        }
        
        private bool CanAddLink() => !string.IsNullOrWhiteSpace(NewLinkUrl);
        
        private void Generate()
        {
            // Implementation would be handled by parent component
            // This is just the UI canvas as requested
        }
        
        private bool CanGenerate()
        {
            return !string.IsNullOrEmpty(TextContent) || Files.Any() || Links.Any();
        }
        
        private async Task UpdateTextMetricsAsync()
        {
            CharacterCount = TextContent?.Length ?? 0;
            WordCount = string.IsNullOrWhiteSpace(TextContent) 
                ? 0 
                : TextContent.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Length;
            
            // Update token count using the AI service
            await UpdateTokenCountAsync();
            
            // Calculate percentage based on tokens vs max context
            if (MaxTokens > 0)
            {
                var pct = (int)Math.Round((double)Math.Min(CurrentTokenCount, MaxTokens) / Math.Max(1, MaxTokens) * 100.0);
                PercentComplete = pct;
            }
            else
            {
                PercentComplete = 0;
            }

            ((RelayCommand)GenerateCommand).NotifyCanExecuteChanged();
        }

        private async Task DebounceUpdateTextMetricsAsync(int delayMs = 200)
        {
            _textMetricsCts?.Cancel();
            _textMetricsCts = new CancellationTokenSource();
            var token = _textMetricsCts.Token;
            try
            {
                await Task.Delay(delayMs, token);
                if (token.IsCancellationRequested) return;
                await UpdateTextMetricsAsync();
            }
            catch (TaskCanceledException) { /* ignore */ }
        }
        
        private async Task UpdateTokenCountAsync()
        {
            if (string.IsNullOrWhiteSpace(TextContent))
            {
                CurrentTokenCount = 0;
                return;
            }
            
            var selectedModel = _mnemoAPI.ai.GetSelectedModel();
            if (string.IsNullOrWhiteSpace(selectedModel))
            {
                CurrentTokenCount = 0;
                return;
            }
            
            try
            {
                var result = await _mnemoAPI.ai.CountTokensAsync(TextContent, selectedModel);
                CurrentTokenCount = result.Success ? result.TokenCount : 0;
            }
            catch
            {
                // Fallback to 0 if tokenization fails
                CurrentTokenCount = 0;
            }
        }
        
        private async Task UpdateModelContextAsync()
        {
            var selectedModel = _mnemoAPI.ai.GetSelectedModel();
            if (string.IsNullOrWhiteSpace(selectedModel))
            {
                MaxTokens = 0;
                return;
            }
            
            try
            {
                var model = await _mnemoAPI.ai.GetModelAsync(selectedModel);
                MaxTokens = (model?.Capabilities?.MaxContextLength ?? 0) - ReservedContext; // Subtract the reserved context from the max context length
                
                // Update current token count as well since model changed
                await UpdateTokenCountAsync();
                
                // Recalculate percentage
                if (MaxTokens > 0)
                {
                    var pct = (int)Math.Round((double)Math.Min(CurrentTokenCount, MaxTokens) / Math.Max(1, MaxTokens) * 100.0);
                    PercentComplete = pct;
                }
                else
                {
                    PercentComplete = 0;
                }
            }
            catch
            {
                MaxTokens = 0;
            }
        }

        private void UpdateBadgeCounts()
        {
            // Treat any non-empty content as a single entry similar to the design's summary
            TextEntryCount = string.IsNullOrWhiteSpace(TextContent) ? 0 : 1;
        }
    }
    
    public enum TabKind
    {
        Text,
        Files,
        Links
    }
    
    public partial class FileItemViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string fileName = string.Empty;
        
        public ICommand? RemoveCommand { get; set; }
        
        public FileItemViewModel(string fileName)
        {
            FileName = fileName;
        }
    }
    
    public partial class LinkItemViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string url = string.Empty;
        
        public ICommand? RemoveCommand { get; set; }
        
        public LinkItemViewModel(string url)
        {
            Url = url;
        }
    }
}
