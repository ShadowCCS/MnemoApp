using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.Core.Models;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Chat.ViewModels;

public class ChatAttachmentViewModel : ViewModelBase
{
    public string Path { get; }
    public string DisplayName { get; }
    public ChatAttachmentKind Kind { get; }
    public ICommand RemoveCommand { get; }

    public ChatAttachmentViewModel(string path, ChatAttachmentKind kind, string? displayName, ICommand removeCommand)
    {
        Path = path;
        Kind = kind;
        DisplayName = displayName ?? System.IO.Path.GetFileName(path);
        RemoveCommand = removeCommand;
    }
}
