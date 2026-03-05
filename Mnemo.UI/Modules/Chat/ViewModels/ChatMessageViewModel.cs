using System;
using System.Collections.Generic;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Chat.ViewModels;

public class ChatMessageViewModel : ViewModelBase
{
    private string _content = string.Empty;
    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

    private bool _isUser;
    public bool IsUser
    {
        get => _isUser;
        set => SetProperty(ref _isUser, value);
    }

    private DateTime _timestamp = DateTime.Now;
    public DateTime Timestamp
    {
        get => _timestamp;
        set => SetProperty(ref _timestamp, value);
    }

    private string? _thoughts;
    public string? Thoughts
    {
        get => _thoughts;
        set => SetProperty(ref _thoughts, value);
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

    private List<ChatAttachmentViewModel>? _attachments;
    /// <summary>Attachments sent with this message (e.g. images). Shown in the bubble for user messages.</summary>
    public List<ChatAttachmentViewModel>? Attachments
    {
        get => _attachments;
        set
        {
            if (SetProperty(ref _attachments, value))
                OnPropertyChanged(nameof(HasAttachments));
        }
    }

    /// <summary>True when this message has attachments to display.</summary>
    public bool HasAttachments => _attachments is { Count: > 0 };

    public bool IsThinking => !string.IsNullOrEmpty(Thoughts);

    private bool _isStreaming;
    /// <summary>True while the assistant message is still being generated (enables live token display).</summary>
    public bool IsStreaming
    {
        get => _isStreaming;
        set => SetProperty(ref _isStreaming, value);
    }
}