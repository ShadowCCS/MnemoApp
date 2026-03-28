using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Components.RightSidebar;

public partial class RightSidebarViewModel : ViewModelBase
{
    public const double MinWidth = 300;
    public const double MaxWidth = 480;
    public const double DefaultWidth = 320;
    /// <summary>Width of the resize handle; overlay so it does not reduce main content area.</summary>
    public const double ResizeHandleWidth = 12;

    private readonly IAIOrchestrator _orchestrator;
    private readonly ILoggerService _logger;
    private readonly ILocalizationService _localizationService;
    private readonly ISettingsService _settingsService;
    private readonly ISkillSystemPromptComposer _skillSystemPromptComposer;
    private readonly ChatTypingPrefetchHelper _typingPrefetch;
    private readonly IChatDatasetLogger _chatDatasetLogger;
    private readonly IRoutingToolHintStore _routingToolHintStore;

    private string _conversationId = Guid.NewGuid().ToString("N");
    private int _turnIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveWidth))]
    [NotifyPropertyChangedFor(nameof(LayoutWidth))]
    private double _expandedWidth = DefaultWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveWidth))]
    [NotifyPropertyChangedFor(nameof(LayoutWidth))]
    private bool _isCollapsed = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private string _inputText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewChatCommand))]
    private bool _isBusy;

    /// <summary>Available assistant modes for the mode dropdown.</summary>
    public IReadOnlyList<string> AssistantModes { get; } = new[] { "General", "Explainer" };

    [ObservableProperty]
    private string _selectedAssistantMode = "General";

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public double EffectiveWidth => IsCollapsed ? 0 : ExpandedWidth;

    /// <summary>Width used for layout.</summary>
    public double LayoutWidth => IsCollapsed ? 0 : ExpandedWidth - ResizeHandleWidth;

    public ICommand ToggleCommand { get; }
    public AsyncRelayCommand SendCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand NewChatCommand { get; }
    public ICommand SuggestionSelectedCommand { get; }

    private CancellationTokenSource? _cts;

    public RightSidebarViewModel(IAIOrchestrator orchestrator, ILoggerService logger, ILocalizationService localizationService, ISettingsService settingsService, ISkillSystemPromptComposer skillSystemPromptComposer, ChatPauseToSendEstimator pauseToSendEstimator, IChatDatasetLogger chatDatasetLogger, IRoutingToolHintStore routingToolHintStore)
    {
        _orchestrator = orchestrator;
        _logger = logger;
        _localizationService = localizationService;
        _settingsService = settingsService;
        _skillSystemPromptComposer = skillSystemPromptComposer;
        _chatDatasetLogger = chatDatasetLogger;
        _routingToolHintStore = routingToolHintStore;
        _typingPrefetch = new ChatTypingPrefetchHelper(orchestrator, pauseToSendEstimator, logger, () => InputText);

        ToggleCommand = new RelayCommand(() => IsCollapsed = !IsCollapsed);
        SendCommand = new AsyncRelayCommand(SendAsync, () => !string.IsNullOrWhiteSpace(InputText) && !IsBusy);
        StopCommand = new RelayCommand(StopGeneration, () => IsBusy);
        NewChatCommand = new RelayCommand(NewChat, () => !IsBusy);
        SuggestionSelectedCommand = new RelayCommand<string>(ApplySuggestion);

        Messages.Add(CreateWelcomeMessage());
    }

    private ChatMessage CreateWelcomeMessage()
    {
        return new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = _localizationService.T("WelcomeMessage", "Chat"),
            Suggestions = new List<string>
            {
                _localizationService.T("SuggestionExplain", "Chat"),
                _localizationService.T("SuggestionQuiz", "Chat"),
                _localizationService.T("SuggestionSummarize", "Chat")
            }
        };
    }

    partial void OnInputTextChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            WarmUpSafe();
        _typingPrefetch.NotifyInputChanged(IsBusy, isRecording: false);
    }

    private void WarmUpSafe()
    {
        _ = WarmUpAsync();
    }

    private async Task WarmUpAsync()
    {
        try
        {
            await _orchestrator.WarmUpLowTierModelAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("RightSidebar", $"WarmUp failed: {ex}");
        }
    }

    private void StopGeneration()
    {
        _cts?.Cancel();
    }

    private void NewChat()
    {
        _routingToolHintStore.Clear(_conversationId);
        _conversationId = Guid.NewGuid().ToString("N");
        _turnIndex = 0;
        Messages.Clear();
        Messages.Add(CreateWelcomeMessage());
    }

    private void ApplySuggestion(string? suggestion)
    {
        if (!string.IsNullOrEmpty(suggestion))
            InputText = suggestion;
    }

    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText) || IsBusy) return;

        await _typingPrefetch.RecordSendPauseAsync().ConfigureAwait(false);

        var userMessage = InputText;
        InputText = string.Empty;

        Messages.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Content = userMessage
        });

        IsBusy = true;

        var aiMessage = new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = string.Empty,
            IsStreaming = true
        };
        Messages.Add(aiMessage);

        _cts = new CancellationTokenSource();

        var logDataset = await _settingsService.GetAsync(ChatDatasetSettings.LoggingEnabledKey, false).ConfigureAwait(false);
        IDisposable? datasetScope = null;
        string? datasetTurnId = null;
        var thisTurnIndex = _turnIndex++;
        if (logDataset)
            datasetScope = ChatDatasetLoggingScope.BeginTurn(out datasetTurnId);

        var conversationHistory = ChatStreamingHelper.BuildConversationHistory(
            Messages, aiMessage, m => m.IsUser, m => m.Content,
            excludeLastUserTurn: true);

        var foundForDataset = false;
        var cancelledForDataset = false;
        string? errorForDataset = null;
        var composedSystemForDataset = string.Empty;
        string? finalAssistantResponseForDataset = null;

        ChatProcessThreadTracker? processThread = null;
        try
        {
            processThread = new ChatProcessThreadTracker(aiMessage.ProcessSteps);

            IProgress<string> pipelineProgress = new Progress<string>(key =>
                Dispatcher.UIThread.Post(() =>
                {
                    processThread!.OnPipelineKey(key, k => _localizationService.T(k, "Chat"));
                    UpdateProcessHeader(aiMessage, processThread);
                }, DispatcherPriority.Background));

            void UpdateContent(string content) =>
                Dispatcher.UIThread.Post(() => { aiMessage.Content = content; }, DispatcherPriority.Background);

            void UpdateReasoning(string reasoning) =>
                Dispatcher.UIThread.Post(() =>
                {
                    aiMessage.Thoughts = string.IsNullOrEmpty(reasoning) ? null : reasoning;
                }, DispatcherPriority.Background);

            var analyzed = await _orchestrator.AnalyzeMessageAsync(userMessage, _cts.Token, pipelineProgress, _conversationId).ConfigureAwait(false);
            var decision = analyzed.IsSuccess && analyzed.Value != null
                ? analyzed.Value
                : new RoutingAndSkillDecision { Complexity = RoutingComplexity.Simple, Skill = "NONE" };
            pipelineProgress.Report(ChatPipelineStatusKeys.ReadingSkill);
            var baseSystemPrompt = ChatStreamingHelper.GetSystemPromptForMode(SelectedAssistantMode);
            composedSystemForDataset = _skillSystemPromptComposer.Compose(baseSystemPrompt, decision.Skill);

            var reveal = await _settingsService.GetAsync("Chat.StreamingReveal", "balanced").ConfigureAwait(false);
            var displayOptions = ChatStreamingDisplayOptions.Parse(reveal);

            Action<ChatDatasetToolCall> onToolCall = tc =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    processThread?.AddToolCall(tc, k => _localizationService.T(k, "Chat"));
                    aiMessage.ThoughtsCount += 1;
                }, DispatcherPriority.Background);
            };

            var (foundResponse, finalContent) = await ChatStreamingHelper.RunStreamingWithHistoryAsync(
                _orchestrator,
                baseSystemPrompt,
                conversationHistory,
                userMessage,
                _cts.Token,
                UpdateContent,
                imageBase64Contents: null,
                pipelineProgress,
                precomputedDecision: decision,
                conversationRoutingKey: _conversationId,
                displayOptions,
                onToolCall,
                onAssistantReasoningUpdate: UpdateReasoning);

            foundForDataset = foundResponse;
            finalAssistantResponseForDataset = foundResponse ? finalContent : null;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                aiMessage.Content = finalContent;
                if (string.IsNullOrWhiteSpace(aiMessage.Thoughts))
                    aiMessage.Thoughts = null;
                processThread?.CompleteThread();
                FinalizeProcessHeader(aiMessage, processThread);
                aiMessage.PipelineStatusText = null;
                aiMessage.IsStreaming = false;
            });

            if (!foundResponse)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    aiMessage.Content = _localizationService.T("ErrorSorry", "Chat"));
            }
        }
        catch (OperationCanceledException)
        {
            cancelledForDataset = true;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (string.IsNullOrEmpty(aiMessage.Content))
                    aiMessage.Content = _localizationService.T("GenerationStopped", "Chat");
                processThread?.CompleteThread();
                FinalizeProcessHeader(aiMessage, processThread);
                aiMessage.PipelineStatusText = null;
                aiMessage.IsStreaming = false;
            });
        }
        catch (Exception ex)
        {
            errorForDataset = ex.Message;
            _logger.Error("RightSidebar", $"Send failed: {ex}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                aiMessage.Content = _localizationService.T("ErrorUnexpected", "Chat");
                processThread?.CompleteThread();
                FinalizeProcessHeader(aiMessage, processThread);
                aiMessage.PipelineStatusText = null;
                aiMessage.IsStreaming = false;
            });
        }
        finally
        {
            try
            {
                if (!string.IsNullOrEmpty(datasetTurnId))
                {
                    var contextForDataset = await Dispatcher.UIThread.InvokeAsync(() =>
                        ChatStreamingHelper.BuildDatasetConversationContextString(
                            Messages, m => m.IsUser, m => m.Content));

                    await _chatDatasetLogger.CommitTurnAsync(new ChatDatasetCommitRequest
                    {
                        TurnId = datasetTurnId,
                        ConversationId = _conversationId,
                        TurnIndex = thisTurnIndex,
                        Source = "right_sidebar",
                        AssistantMode = SelectedAssistantMode,
                        LatestUserMessage = userMessage,
                        ConversationContext = contextForDataset,
                        ComposedSystemPrompt = composedSystemForDataset,
                        FinalAssistantResponse = finalAssistantResponseForDataset,
                        Outcome = new ChatDatasetOutcomeSection
                        {
                            FoundResponse = foundForDataset,
                            Cancelled = cancelledForDataset,
                            Error = errorForDataset
                        }
                    }).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("RightSidebar", $"Dataset log commit failed: {ex}");
            }

            datasetScope?.Dispose();

            var cts = _cts;
            _cts = null;
            cts?.Dispose();
            Dispatcher.UIThread.Post(() => IsBusy = false);
        }
    }

    private static void UpdateProcessHeader(ChatMessage message, ChatProcessThreadTracker tracker)
    {
        var e = tracker.Elapsed;
        message.ElapsedText = $"{(int)e.TotalMinutes:D2}:{e.Seconds:D2}";
        message.ProcessHeaderText = tracker.ActiveStepLabel ?? "Thinking…";
    }

    private static void FinalizeProcessHeader(ChatMessage message, ChatProcessThreadTracker? tracker)
    {
        if (tracker == null) return;
        var e = tracker.Elapsed;
        message.ElapsedText = $"{(int)e.TotalMinutes:D2}:{e.Seconds:D2}";
        var steps = message.ThoughtsCount;
        message.ProcessHeaderText = steps > 0
            ? $"Thought process ({steps} steps)"
            : "Thought process";
    }
}
