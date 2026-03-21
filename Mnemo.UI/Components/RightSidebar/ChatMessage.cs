using System.Collections.Generic;
using System.Collections.ObjectModel;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Components.RightSidebar;

public enum MessageRole
{
    User,
    Assistant
}

public class ChatMessage : ViewModelBase
{
    private MessageRole _role;
    public MessageRole Role
    {
        get => _role;
        set
        {
            if (SetProperty(ref _role, value))
            {
                OnPropertyChanged(nameof(IsUser));
                OnPropertyChanged(nameof(IsAssistant));
            }
        }
    }

    private string _content = string.Empty;
    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

    private string? _thoughts;
    public string? Thoughts
    {
        get => _thoughts;
        set
        {
            if (SetProperty(ref _thoughts, value))
                OnPropertyChanged(nameof(IsThinking));
        }
    }

    private List<string>? _sources;
    public List<string>? Sources
    {
        get => _sources;
        set => SetProperty(ref _sources, value);
    }

    private List<string>? _suggestions;
    public List<string>? Suggestions
    {
        get => _suggestions;
        set => SetProperty(ref _suggestions, value);
    }

    public bool IsThinking => !string.IsNullOrEmpty(Thoughts);

    private string? _pipelineStatusText;
    /// <summary>Localized pipeline label while routing or loading the model (cleared when reply text appears).</summary>
    public string? PipelineStatusText
    {
        get => _pipelineStatusText;
        set
        {
            if (SetProperty(ref _pipelineStatusText, value))
                OnPropertyChanged(nameof(HasPipelineStatus));
        }
    }

    public bool HasPipelineStatus => !string.IsNullOrEmpty(_pipelineStatusText);

    private bool _isStreaming;
    /// <summary>True while the assistant message is still being generated.</summary>
    public bool IsStreaming
    {
        get => _isStreaming;
        set => SetProperty(ref _isStreaming, value);
    }

    public bool IsUser => Role == MessageRole.User;
    public bool IsAssistant => Role == MessageRole.Assistant;
}
