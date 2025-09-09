using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MnemoApp.Core.Common;

namespace MnemoApp.UI.Components
{
    public partial class InputBuilderViewModel : ViewModelBase
    {
        [ObservableProperty]
        private int usableContext = 0;

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
        
        public InputBuilderViewModel()
        {
            SelectTextTabCommand = new RelayCommand(SelectTextTab);
            SelectFilesTabCommand = new RelayCommand(SelectFilesTab);
            SelectLinksTabCommand = new RelayCommand(SelectLinksTab);
            AddLinkCommand = new RelayCommand(AddLink, CanAddLink);
            GenerateCommand = new RelayCommand(Generate, CanGenerate);
            
            // Initialize with default tab selection based on visibility
            EnsureValidTabSelection();
            
            // Listen to text content changes for character/word count
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TextContent))
                {
                    UpdateTextMetrics();
                    UpdateBadgeCounts();
                }
                if (e.PropertyName == nameof(NewLinkUrl))
                {
                    ((RelayCommand)AddLinkCommand).NotifyCanExecuteChanged();
                }
            };

            Files.CollectionChanged += (_, __) => UpdateBadgeCounts();
            Links.CollectionChanged += (_, __) => UpdateBadgeCounts();
        }

        public void ApplyConfigurationFromControl(int usableContext, string inputMethodsCsv)
        {
            UsableContext = usableContext;
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
                    SelectTextTab();
                else if (ShowFilesTab)
                    SelectFilesTab();
                else if (ShowLinksTab)
                    SelectLinksTab();
            }
        }
        
        private void SelectTextTab()
        {
            if (!ShowTextTab) return;
            
            IsTextTabActive = true;
            IsFilesTabActive = false;
            IsLinksTabActive = false;
        }
        
        private void SelectFilesTab()
        {
            if (!ShowFilesTab) return;
            
            IsTextTabActive = false;
            IsFilesTabActive = true;
            IsLinksTabActive = false;
        }
        
        private void SelectLinksTab()
        {
            if (!ShowLinksTab) return;
            
            IsTextTabActive = false;
            IsFilesTabActive = false;
            IsLinksTabActive = true;
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
        
        private void UpdateTextMetrics()
        {
            CharacterCount = TextContent?.Length ?? 0;
            WordCount = string.IsNullOrWhiteSpace(TextContent) 
                ? 0 
                : TextContent.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Length;
            // If usable context provided, compute percent
            if (UsableContext > 0)
            {
                var pct = (int)System.Math.Round((double)System.Math.Min(CharacterCount, UsableContext) / System.Math.Max(1, UsableContext) * 100.0);
                PercentComplete = pct;
            }

            ((RelayCommand)GenerateCommand).NotifyCanExecuteChanged();
        }

        private void UpdateBadgeCounts()
        {
            // Treat any non-empty content as a single entry similar to the design's summary
            TextEntryCount = string.IsNullOrWhiteSpace(TextContent) ? 0 : 1;
        }
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
