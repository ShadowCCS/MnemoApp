using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Chat.ViewModels;

/// <summary>One row in the chat module history sidebar.</summary>
public sealed class ChatConversationRowViewModel : ViewModelBase
{
    public ChatConversationRowViewModel(string conversationId, System.Action<string> onSelected)
    {
        ConversationId = conversationId;
        SelectCommand = new RelayCommand(() => onSelected(ConversationId));
    }

    public string ConversationId { get; }

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public ICommand SelectCommand { get; }
}
