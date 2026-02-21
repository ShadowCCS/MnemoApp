using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public RightSidebarViewModel(IAIOrchestrator orchestrator, ILoggerService logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;

        ToggleCommand = new RelayCommand(() => IsCollapsed = !IsCollapsed);
        SendCommand = new AsyncRelayCommand(SendAsync, () => !string.IsNullOrWhiteSpace(InputText) && !IsBusy);
        StopCommand = new RelayCommand(StopGeneration, () => IsBusy);
        NewChatCommand = new RelayCommand(NewChat, () => !IsBusy);
        SuggestionSelectedCommand = new RelayCommand<string>(ApplySuggestion);

        Messages.Add(new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = "Hi, I'm your Mnemo assistant. Ask me to explain a concept, summarize a lesson, or help you practice.",
            Suggestions = new List<string> { "Explain this page", "Quiz me", "Summarize" }
        });
    }

    partial void OnInputTextChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            WarmUpSafe();
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
            _logger.Error("RightSidebar", $"WarmUp failed: {ex}");
        }
    }

    private void StopGeneration()
    {
        _cts?.Cancel();
    }

    private void NewChat()
    {
        Messages.Clear();
        Messages.Add(new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = "Hi, I'm your Mnemo assistant. Ask me to explain a concept, summarize a lesson, or help you practice.",
            Suggestions = new List<string> { "Explain this page", "Quiz me", "Summarize" }
        });
    }

    private void ApplySuggestion(string? suggestion)
    {
        if (!string.IsNullOrEmpty(suggestion))
            InputText = suggestion;
    }

    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText) || IsBusy) return;

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
                    aiMessage.Content = "I'm sorry, I couldn't generate a response.");
            }
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (string.IsNullOrEmpty(aiMessage.Content))
                    aiMessage.Content = "Generation stopped.";
                aiMessage.IsStreaming = false;
            });
        }
        catch (Exception ex)
        {
            _logger.Error("RightSidebar", $"Send failed: {ex}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                aiMessage.Content = "An unexpected error occurred. Please try again later.";
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
