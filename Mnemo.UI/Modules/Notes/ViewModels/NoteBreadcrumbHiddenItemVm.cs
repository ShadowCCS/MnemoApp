using System;
using CommunityToolkit.Mvvm.Input;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Notes.ViewModels;

public sealed class NoteBreadcrumbHiddenItemVm : ViewModelBase
{
    public NoteBreadcrumbHiddenItemVm(string text, string? noteId, Action<string?> navigateToNote)
    {
        Text = NoteBreadcrumbTitleFormatter.ToDisplayText(text);
        ToolTipTip = NoteBreadcrumbTitleFormatter.ToolTipFor(text);
        NoteId = noteId;
        NavigateCommand = new RelayCommand(
            () => navigateToNote(NoteId),
            () => !string.IsNullOrEmpty(NoteId));
    }

    public string Text { get; }

    public object? ToolTipTip { get; }

    public string? NoteId { get; }

    public bool IsNavigable => !string.IsNullOrEmpty(NoteId);

    public RelayCommand NavigateCommand { get; }
}
