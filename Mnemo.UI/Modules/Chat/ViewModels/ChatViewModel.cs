using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI.Components.Overlays;
using Mnemo.UI.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Chat.ViewModels;

public class ChatViewModel : ViewModelBase
{
    /// <summary>Distance from bottom (px) below which we consider "at bottom" for scroll-to-bottom button.</summary>
    public const double ScrollToBottomThreshold = 20;

    private static readonly ICommand NoOpAttachmentCommand = new RelayCommand<ChatAttachmentViewModel>(_ => { });

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"
    };

    private readonly IAIOrchestrator _orchestrator;
    private readonly ILoggerService _logger;
    private readonly ILocalizationService _localizationService;
    private readonly IOverlayService _overlayService;
    private readonly ISpeechRecognitionService _speechService;
    private readonly ISettingsService _settingsService;
    private readonly ChatTypingPrefetchHelper _typingPrefetch;

    /// <summary>When recording started, if append mode: content that was in the input before dictation.</summary>
    private string _inputTextBeforeRecording = string.Empty;
    private bool _wipeInputForDictation;

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
                _typingPrefetch.NotifyInputChanged(IsBusy, IsRecording);
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

    /// <summary>When true, new content (e.g. streaming) will auto-scroll to bottom. Becomes false when user scrolls up; restored when user clicks "Scroll to bottom".</summary>
    private bool _isAutoScrollAttached = true;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();

    /// <summary>Available assistant modes for the mode dropdown.</summary>
    public IReadOnlyList<string> AssistantModes { get; } = new[] { "General", "Explainer" };

    private string _selectedAssistantMode = "General";
    public string SelectedAssistantMode
    {
        get => _selectedAssistantMode;
        set => SetProperty(ref _selectedAssistantMode, value);
    }

    public ObservableCollection<ChatAttachmentViewModel> PendingAttachments { get; } = new();

    public ICommand SendMessageCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand NewChatCommand { get; }
    public ICommand SuggestionSelectedCommand { get; }
    public ICommand ScrollToBottomCommand { get; }
    public ICommand RemoveAttachmentCommand { get; }
    public ICommand OpenImagePreviewCommand { get; }
    public ICommand StartRecordingCommand { get; }
    public ICommand StopRecordingCommand { get; }
    public ICommand CancelRecordingCommand { get; }

    /// <summary>Raised when the view should scroll to the end (e.g. after new message or user clicked "Scroll to bottom").</summary>
    public event EventHandler? RequestScrollToBottom;

    private CancellationTokenSource? _cts;
    private ChatMessageViewModel? _lastMessageSubscribed;
    private CancellationTokenSource? _recordingDurationCts;

    private bool _isRecording;
    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            if (SetProperty(ref _isRecording, value))
            {
                OnPropertyChanged(nameof(WatermarkText));
                ((AsyncRelayCommand)StartRecordingCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)StopRecordingCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)CancelRecordingCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)SendMessageCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string WatermarkText => IsRecording 
        ? _localizationService.T("Listening", "Chat") 
        : _localizationService.T("AskPlaceholder", "Chat");

    private TimeSpan _recordingDuration;
    public string RecordingDurationText => _recordingDuration.ToString(@"mm\:ss");

    public ChatViewModel(IAIOrchestrator orchestrator, ILoggerService logger, ILocalizationService localizationService, IOverlayService overlayService, ISpeechRecognitionService speechService, ISettingsService settingsService, ChatPauseToSendEstimator pauseToSendEstimator)
    {
        _orchestrator = orchestrator;
        _logger = logger;
        _localizationService = localizationService;
        _overlayService = overlayService;
        _speechService = speechService;
        _settingsService = settingsService;
        _typingPrefetch = new ChatTypingPrefetchHelper(orchestrator, pauseToSendEstimator, logger, () => InputText);

        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, () => !string.IsNullOrWhiteSpace(InputText) && !IsBusy && !IsRecording);
        StopCommand = new RelayCommand(StopGeneration, () => IsBusy);
        NewChatCommand = new RelayCommand(NewChat, () => !IsBusy && !IsRecording);
        SuggestionSelectedCommand = new RelayCommand<string>(ApplySuggestion);
        ScrollToBottomCommand = new RelayCommand(OnScrollToBottom);
        RemoveAttachmentCommand = new RelayCommand<ChatAttachmentViewModel>(a =>
        {
            if (a != null)
                PendingAttachments.Remove(a);
        });
        OpenImagePreviewCommand = new RelayCommand<string>(OpenImagePreview);

        StartRecordingCommand = new AsyncRelayCommand(StartRecordingAsync, () => !IsBusy && !IsRecording);
        StopRecordingCommand = new AsyncRelayCommand(StopRecordingAsync, () => IsRecording);
        CancelRecordingCommand = new AsyncRelayCommand(CancelRecordingAsync, () => IsRecording);

        Messages.CollectionChanged += OnMessagesCollectionChanged;

        Messages.Add(CreateWelcomeMessage());
        OnPropertyChanged(nameof(WatermarkText));
    }

    private void OnPartialTranscript(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            InputText = _wipeInputForDictation
                ? text
                : string.IsNullOrWhiteSpace(_inputTextBeforeRecording)
                    ? text
                    : _inputTextBeforeRecording + (string.IsNullOrWhiteSpace(text) ? "" : " " + text);
        });
    }

    private async Task StartRecordingAsync()
    {
        if (IsRecording) return;
        _wipeInputForDictation = await _settingsService.GetAsync("Chat.WipeInputForDictation", false).ConfigureAwait(false);
        _inputTextBeforeRecording = _wipeInputForDictation ? string.Empty : InputText;
        try
        {
            await _speechService.StartRecordingAsync(CancellationToken.None, OnPartialTranscript).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsRecording = true;
                _recordingDuration = TimeSpan.Zero;
                OnPropertyChanged(nameof(RecordingDurationText));

                _recordingDurationCts = new CancellationTokenSource();
                var ct = _recordingDurationCts.Token;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                            if (ct.IsCancellationRequested) break;
                            Dispatcher.UIThread.Post(() =>
                            {
                                _recordingDuration = _recordingDuration.Add(TimeSpan.FromSeconds(1));
                                OnPropertyChanged(nameof(RecordingDurationText));
                            });
                        }
                    }
                    catch (OperationCanceledException) { }
                }, CancellationToken.None);
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Chat", $"Failed to start recording: {ex}");
        }
    }

    private async Task StopRecordingAsync()
    {
        if (!IsRecording) return;

        _recordingDurationCts?.Cancel();
        _recordingDurationCts?.Dispose();
        _recordingDurationCts = null;
        IsRecording = false;

        IsBusy = true;
        try
        {
            var result = await _speechService.StopAndTranscribeAsync(CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value))
                {
                    InputText = _wipeInputForDictation
                        ? result.Value
                        : string.IsNullOrWhiteSpace(_inputTextBeforeRecording)
                            ? result.Value
                            : _inputTextBeforeRecording + " " + result.Value;
                }
                else if (!result.IsSuccess)
                    _logger.Error("Chat", result.ErrorMessage ?? "Speech recognition failed.");
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Chat", $"Speech recognition failed: {ex}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false);
        }
    }

    private async Task CancelRecordingAsync()
    {
        if (!IsRecording) return;

        _recordingDurationCts?.Cancel();
        _recordingDurationCts?.Dispose();
        _recordingDurationCts = null;

        await _speechService.CancelRecordingAsync(CancellationToken.None).ConfigureAwait(false);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsRecording = false;
            _recordingDuration = TimeSpan.Zero;
            OnPropertyChanged(nameof(RecordingDurationText));
            InputText = _inputTextBeforeRecording;
        });
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
            _logger.Error("Chat", $"WarmUp failed: {ex}");
        }
    }

    /// <summary>Called by the view when scroll position changes; updates <see cref="ShowScrollToBottomButton"/> and auto-scroll attachment.</summary>
    public void NotifyScrollPosition(double offsetY, double extentHeight, double viewportHeight)
    {
        var atBottom = extentHeight - viewportHeight - offsetY <= ScrollToBottomThreshold;
        if (!atBottom)
        {
            ShowScrollToBottomButton = true;
            _isAutoScrollAttached = false;
        }
        else
        {
            ShowScrollToBottomButton = false;
            _isAutoScrollAttached = true;
        }
    }

    private void OnScrollToBottom()
    {
        _isAutoScrollAttached = true;
        ShowScrollToBottomButton = false;
        RequestScrollToBottom?.Invoke(this, EventArgs.Empty);
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        SubscribeToLastMessage();
        if (_isAutoScrollAttached)
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
        if (e.PropertyName == nameof(ChatMessageViewModel.Content) && _isAutoScrollAttached)
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

    /// <summary>Adds an attachment (file or image path). Call from view after file picker or screenshot capture.</summary>
    public void AddPendingAttachment(string path, ChatAttachmentKind kind, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        var item = new ChatAttachmentViewModel(path, kind, displayName, RemoveAttachmentCommand);
        PendingAttachments.Add(item);
    }

    /// <summary>Determines if a file path should be treated as an image for the vision model.</summary>
    public static bool IsImagePath(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && ImageExtensions.Contains(ext);
    }

    /// <summary>Opens an overlay preview for the given image path. No-op if path is invalid or file missing.</summary>
    private void OpenImagePreview(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        var overlay = new ImagePreviewOverlay { ImagePath = path };
        var options = new OverlayOptions
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ShowBackdrop = true,
            CloseOnOutsideClick = true,
            CloseOnEscape = true
        };
        var id = _overlayService.CreateOverlay(overlay, options, "ImagePreview");
        overlay.CloseRequested += () => _overlayService.CloseOverlay(id);
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
        PendingAttachments.Clear();
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

        await _typingPrefetch.RecordSendPauseAsync().ConfigureAwait(false);

        var userMessage = InputText;
        InputText = string.Empty;

        var imagePaths = new List<string>();
        var pendingList = PendingAttachments.ToList();
        foreach (var a in pendingList)
        {
            if (a.Kind == ChatAttachmentKind.Image)
                imagePaths.Add(a.Path);
        }

        var messageAttachments = pendingList
            .Select(a => new ChatAttachmentViewModel(a.Path, a.Kind, a.DisplayName, NoOpAttachmentCommand))
            .ToList();
        PendingAttachments.Clear();

        Messages.Add(new ChatMessageViewModel
        {
            Content = userMessage,
            IsUser = true,
            Attachments = messageAttachments.Count > 0 ? messageAttachments : null
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

        List<string>? imageBase64 = null;
        if (imagePaths.Count > 0)
        {
            try
            {
                imageBase64 = new List<string>(imagePaths.Count);
                foreach (var p in imagePaths)
                {
                    var bytes = await File.ReadAllBytesAsync(p, _cts.Token).ConfigureAwait(false);
                    imageBase64.Add(Convert.ToBase64String(bytes));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Chat", $"Failed to read image(s) for upload: {ex}");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    aiMessage.Content = _localizationService.T("ErrorUnexpected", "Chat");
                    aiMessage.PipelineStatusText = null;
                    aiMessage.IsStreaming = false;
                });
                IsBusy = false;
                return;
            }
        }

        try
        {
            var context = ChatStreamingHelper.BuildContextFromMessages(
                Messages, aiMessage, m => m.IsUser, m => m.Content);
            var fullPrompt = string.IsNullOrEmpty(context)
                ? userMessage
                : $"{context}\nUser: {userMessage}";

            var pipelineProgress = new Progress<string>(key =>
                Dispatcher.UIThread.Post(() => aiMessage.PipelineStatusText = _localizationService.T(key, "Chat")));

            void UpdateContent(string content) =>
                Dispatcher.UIThread.Post(() =>
                {
                    aiMessage.Content = content;
                    if (!string.IsNullOrEmpty(content))
                        aiMessage.PipelineStatusText = null;
                });

            var reveal = await _settingsService.GetAsync("Chat.StreamingReveal", "balanced").ConfigureAwait(false);
            var displayOptions = ChatStreamingDisplayOptions.Parse(reveal);

            var (foundResponse, finalContent) = await ChatStreamingHelper.RunStreamingAsync(
                _orchestrator,
                ChatStreamingHelper.GetSystemPromptForMode(SelectedAssistantMode),
                fullPrompt,
                _cts.Token,
                UpdateContent,
                imageBase64,
                routingUserMessage: userMessage,
                pipelineProgress,
                displayOptions);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                aiMessage.Content = finalContent;
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
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (string.IsNullOrEmpty(aiMessage.Content))
                    aiMessage.Content = _localizationService.T("GenerationStopped", "Chat");
                aiMessage.PipelineStatusText = null;
                aiMessage.IsStreaming = false;
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Chat", $"SendMessage failed: {ex}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                aiMessage.Content = _localizationService.T("ErrorUnexpected", "Chat");
                aiMessage.PipelineStatusText = null;
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
