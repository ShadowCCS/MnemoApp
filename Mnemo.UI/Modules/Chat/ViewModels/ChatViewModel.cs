using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Chat.ViewModels;

public class ChatViewModel : ViewModelBase
{
    private readonly IAIOrchestrator _orchestrator;
    private readonly ILoggerService _logger;

    private string _inputText = string.Empty;
    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetProperty(ref _inputText, value))
            {
                ((AsyncRelayCommand)SendMessageCommand).NotifyCanExecuteChanged();
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

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();

    public ICommand SendMessageCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand NewChatCommand { get; }

    private CancellationTokenSource? _cts;

    public ChatViewModel(IAIOrchestrator orchestrator, ILoggerService logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;

        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, () => !string.IsNullOrWhiteSpace(InputText) && !IsBusy);
        StopCommand = new RelayCommand(StopGeneration, () => IsBusy);
        NewChatCommand = new RelayCommand(NewChat, () => !IsBusy);
        
        // Add a welcome message
        Messages.Add(new ChatMessageViewModel 
        { 
            Content = "Hello! I'm your AI assistant. How can I help you today?", 
            IsUser = false 
        });
    }

    private void StopGeneration()
    {
        _cts?.Cancel();
    }

    private void NewChat()
    {
        Messages.Clear();
        Messages.Add(new ChatMessageViewModel 
        { 
            Content = "Hello! I'm your AI assistant. How can I help you today?", 
            IsUser = false 
        });
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
            IsUser = false
        };
        Messages.Add(aiMessage);

        _cts = new CancellationTokenSource();

        try
        {
            // Build simple context from last 10 messages
            var context = string.Empty;
            var recentMessages = Messages.TakeLast(11).Where(m => m != aiMessage).ToList();
            if (recentMessages.Count > 1)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Previous conversation history:");
                foreach (var msg in recentMessages)
                {
                    sb.AppendLine($"{(msg.IsUser ? "User" : "Assistant")}: {msg.Content}");
                }
                context = sb.ToString();
            }

            var fullPrompt = string.IsNullOrEmpty(context) 
                ? userMessage 
                : $"{context}\nUser: {userMessage}";

            var foundResponse = false;
            var systemPrompt = @"You are a helpful AI assistant in the Mnemo application.

When answering:
- Use Markdown formatting
- Use tables for comparisons or structured data (regular markdown format is supported, if asked for a table do not make it in a code block)
- Use LaTeX for equations when appropriate
- Prefer clarity and structure over prose";

            var buffer = new StringBuilder();
            var lastUpdate = DateTime.Now;

            await foreach (var token in _orchestrator.PromptStreamingAsync(systemPrompt, fullPrompt, _cts?.Token ?? default))
            {
                buffer.Append(token);
                foundResponse = true;

                // Update UI on UI thread every ~50ms or on newline so content appears as it's generated
                if ((DateTime.Now - lastUpdate).TotalMilliseconds > 50 || token.Contains('\n'))
                {
                    var content = buffer.ToString();
                    Dispatcher.UIThread.Post(() => aiMessage.Content = content);
                    lastUpdate = DateTime.Now;
                }
            }

            // Final update on UI thread so last chunk is visible immediately
            var finalContent = buffer.ToString();
            Dispatcher.UIThread.Post(() => aiMessage.Content = finalContent);
            
            if (!foundResponse)
            {
                Dispatcher.UIThread.Post(() => aiMessage.Content = "I'm sorry, I couldn't generate a response.");
            }
        }
        catch (OperationCanceledException)
        {
            if (string.IsNullOrEmpty(aiMessage.Content))
            {
                Dispatcher.UIThread.Post(() => aiMessage.Content = "Generation stopped.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Chat", $"Exception in chat: {ex.Message}");
            Dispatcher.UIThread.Post(() => aiMessage.Content = "An unexpected error occurred. Please try again later.");
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }
}

