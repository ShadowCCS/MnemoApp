using System;
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
}




