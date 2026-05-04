using System;
using CommunityToolkit.Mvvm.Input;

namespace Mnemo.UI.Modules.Notes.ViewModels;

public sealed class NoteBreadcrumbCrumbPieceVm : NoteBreadcrumbPieceBase
{
    private string? _noteId;
    private bool _isCurrent;

    public string Text { get; init; } = string.Empty;

    /// <summary>Full title when <see cref="Text"/> was shortened; unset when no tooltip is needed.</summary>
    public object? ToolTipTip { get; init; }

    /// <summary>When null, segment is a folder label (not navigable).</summary>
    public string? NoteId
    {
        get => _noteId;
        init
        {
            _noteId = value;
            if (value != null && !IsCurrent)
            {
                NavigateCommand = new RelayCommand(() => OnNavigate?.Invoke(value));
            }
        }
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        init
        {
            _isCurrent = value;
            if (NoteId != null && !value)
            {
                NavigateCommand = new RelayCommand(() => OnNavigate?.Invoke(NoteId));
            }
        }
    }

    public bool IsClickable => NoteId != null && !IsCurrent;

    public IRelayCommand? NavigateCommand { get; private set; }

    /// <summary>Called by the parent ViewModel to wire up navigation.</summary>
    internal Action<string?>? OnNavigate { get; set; }
}
