using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Services;
using Mnemo.UI.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Chat.ViewModels;

public class ChatViewModel : ViewModelBase
{
    /// <summary>Distance from bottom (px) below which we consider "at bottom" for scroll-to-bottom button.</summary>
    public const double ScrollToBottomThreshold = 20;

    private readonly IAIOrchestrator _orchestrator;
    private readonly ILoggerService _logger;
    private readonly ILocalizationService _localizationService;

    private string _inputText = string.Empty;
    public string InputText
    {
        get => _inputText;
        set
        {
            var wasEmpty = string.IsNullOrWhiteSpace(_inputText);
            if (SetProperty(ref _inputText, value))
            {
                ((AsyncRelayCommand)SendMessageCommand).NotifyCanExecuteChanged();
                if (wasEmpty && !string.IsNullOrWhiteSpace(value))
                    WarmUpSafe();
            }
        }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((AsyncRelayCommand)SendMessageCommand).NotifyCanExecuteChanged();
                ((RelayCommand)StopCommand).NotifyCanExecuteChanged();
                ((RelayCommand)NewChatCommand).NotifyCanExecuteChanged();
            }
        }
    }

    private bool _showScrollToBottomButton;
    /// <summary>True when the user has scrolled up and the "Scroll to bottom" button should be visible.</summary>
    public bool ShowScrollToBottomButton
    {
        get => _showScrollToBottomButton;
        set => SetProperty(ref _showScrollToBottomButton, value);
    }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();

    /// <summary>Available assistant modes for the mode dropdown.</summary>
    public IReadOnlyList<string> AssistantModes { get; } = new[] { "General", "Explainer" };

    private string _selectedAssistantMode = "General";
    public string SelectedAssistantMode
    {
        get => _selectedAssistantMode;
        set => SetProperty(ref _selectedAssistantMode, value);
    }

    public ICommand SendMessageCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand NewChatCommand { get; }
    public ICommand SuggestionSelectedCommand { get; }
    public ICommand ScrollToBottomCommand { get; }

    /// <summary>Raised when the view should scroll to the end (e.g. after new message or user clicked "Scroll to bottom").</summary>
    public event EventHandler? RequestScrollToBottom;

    private CancellationTokenSource? _cts;
    private ChatMessageViewModel? _lastMessageSubscribed;

    public ChatViewModel(IAIOrchestrator orchestrator, ILoggerService logger, ILocalizationService localizationService)
    {
        _orchestrator = orchestrator;
        _logger = logger;
        _localizationService = localizationService;

        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, () => !string.IsNullOrWhiteSpace(InputText) && !IsBusy);
        StopCommand = new RelayCommand(StopGeneration, () => IsBusy);
        NewChatCommand = new RelayCommand(NewChat, () => !IsBusy);
        SuggestionSelectedCommand = new RelayCommand<string>(ApplySuggestion);
        ScrollToBottomCommand = new RelayCommand(OnScrollToBottom);

        Messages.CollectionChanged += OnMessagesCollectionChanged;

        Messages.Add(CreateWelcomeMessage());
    }

    private void WarmUpSafe()
    {
        _ = WarmUpAsync();
    }

    private async Task WarmUpAsync()
    {
        try
        {
            await _orchestrator.WarmUpFastModelAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Chat", $"WarmUp failed: {ex}");
        }
    }

    /// <summary>Called by the view when scroll position changes; updates <see cref="ShowScrollToBottomButton"/>.</summary>
    public void NotifyScrollPosition(double offsetY, double extentHeight, double viewportHeight)
    {
        var atBottom = extentHeight - viewportHeight - offsetY <= ScrollToBottomThreshold;
        if (!atBottom)
            ShowScrollToBottomButton = true;
        else
            ShowScrollToBottomButton = false;
    }

    private void OnScrollToBottom()
    {
        ShowScrollToBottomButton = false;
        RequestScrollToBottom?.Invoke(this, EventArgs.Empty);
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        SubscribeToLastMessage();
        RequestScrollToBottom?.Invoke(this, EventArgs.Empty);
    }

    private void SubscribeToLastMessage()
    {
        if (_lastMessageSubscribed != null)
        {
            _lastMessageSubscribed.PropertyChanged -= OnLastMessageContentChanged;
            _lastMessageSubscribed = null;
        }
        if (Messages.Count == 0) return;
        var last = Messages[Messages.Count - 1];
        _lastMessageSubscribed = last;
        last.PropertyChanged += OnLastMessageContentChanged;
    }

    private void OnLastMessageContentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatMessageViewModel.Content))
            RequestScrollToBottom?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySuggestion(string? suggestion)
    {
        if (!string.IsNullOrEmpty(suggestion))
            InputText = suggestion;
    }

    private void StopGeneration()
    {
        _cts?.Cancel();
    }

    private void NewChat()
    {
        Messages.CollectionChanged -= OnMessagesCollectionChanged;
        if (_lastMessageSubscribed != null)
        {
            _lastMessageSubscribed.PropertyChanged -= OnLastMessageContentChanged;
            _lastMessageSubscribed = null;
        }
        Messages.Clear();
        Messages.CollectionChanged += OnMessagesCollectionChanged;
        Messages.Add(CreateWelcomeMessage());
    }

    private ChatMessageViewModel CreateWelcomeMessage()
    {
        return new ChatMessageViewModel
        {
            Content = _localizationService.T("WelcomeMessage", "Chat"),
            IsUser = false,
            Suggestions = new List<string>
            {
                _localizationService.T("SuggestionExplain", "Chat"),
                _localizationService.T("SuggestionQuiz", "Chat"),
                _localizationService.T("SuggestionSummarize", "Chat")
            }
        };
    }

    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText) || IsBusy) return;

        var userMessage = InputText;
        InputText = string.Empty;

        Messages.Add(new ChatMessageViewModel
        {
            Content = userMessage,
            IsUser = true
        });

        IsBusy = true;

        var aiMessage = new ChatMessageViewModel
        {
            Content = string.Empty,
            IsUser = false,
            IsStreaming = true
        };
        Messages.Add(aiMessage);

        _cts = new CancellationTokenSource();

        try
        {
            var context = ChatStreamingHelper.BuildContextFromMessages(
                Messages, aiMessage, m => m.IsUser, m => m.Content);
            var fullPrompt = string.IsNullOrEmpty(context)
                ? userMessage
                : $"{context}\nUser: {userMessage}";

            void UpdateContent(string content) =>
                Dispatcher.UIThread.Post(() => aiMessage.Content = content);

            var (foundResponse, finalContent) = await ChatStreamingHelper.RunStreamingAsync(
                _orchestrator,
                ChatStreamingHelper.GetSystemPromptForMode(SelectedAssistantMode),
                fullPrompt,
                _cts.Token,
                UpdateContent);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                aiMessage.Content = finalContent;
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
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (string.IsNullOrEmpty(aiMessage.Content))
                    aiMessage.Content = _localizationService.T("GenerationStopped", "Chat");
                aiMessage.IsStreaming = false;
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Chat", $"SendMessage failed: {ex}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                aiMessage.Content = _localizationService.T("ErrorUnexpected", "Chat");
                aiMessage.IsStreaming = false;
            });
        }
        finally
        {
            var cts = _cts;
            _cts = null;
            cts?.Dispose();
            Dispatcher.UIThread.Post(() => IsBusy = false);
        }
    }
}
