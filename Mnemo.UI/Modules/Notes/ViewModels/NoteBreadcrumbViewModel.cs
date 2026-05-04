using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Notes.ViewModels;

public partial class NoteBreadcrumbViewModel : ViewModelBase
{
    private const int MaxVisibleItems = 4;

    private readonly INoteFolderService _folderService;
    private readonly Action<string?> _navigateToNote;
    private readonly Func<IReadOnlyCollection<Note>> _getAllNotes;

    [ObservableProperty]
    private ObservableCollection<NoteBreadcrumbPieceBase> _pieces = new();

    [ObservableProperty]
    private bool _isVisible;

    public NoteBreadcrumbViewModel(
        Func<IReadOnlyCollection<Note>> getAllNotes,
        INoteFolderService folderService,
        Action<string?> navigateToNote)
    {
        _getAllNotes = getAllNotes;
        _folderService = folderService;
        _navigateToNote = navigateToNote;
    }

    public void BuildForNote(Note? note, IReadOnlyDictionary<string, NoteFolder> foldersById)
    {
        Pieces.Clear();
        IsVisible = note != null;

        if (note == null)
            return;

        var allNotes = _getAllNotes();
        var chain = BuildNoteChain(note, allNotes);
        var segments = new List<Segment>();

        // Add folder/location segment if the note has a folder
        if (!string.IsNullOrEmpty(note.FolderPath))
        {
            var folderParts = note.FolderPath.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (folderParts.Length > 0)
            {
                segments.Add(new Segment(folderParts[0], null, SegmentType.Folder));
                for (int i = 1; i < folderParts.Length; i++)
                {
                    segments.Add(new Segment(folderParts[i], null, SegmentType.Folder));
                }
            }
        }
        else if (note.FolderId != null && foldersById.TryGetValue(note.FolderId, out var folder))
        {
            var folderPath = GetFolderPath(folder, foldersById);
            foreach (var folderName in folderPath)
            {
                segments.Add(new Segment(folderName, null, SegmentType.Folder));
            }
        }

        // Add note chain segments (root note -> ... -> parent -> current)
        for (int i = 0; i < chain.Count; i++)
        {
            var n = chain[i];
            bool isCurrent = i == chain.Count - 1;
            segments.Add(new Segment(n.Title ?? string.Empty, n.NoteId, isCurrent ? SegmentType.Current : SegmentType.Note));
        }

        // If only one segment, show it as current
        if (segments.Count == 0)
        {
            segments.Add(new Segment(note.Title ?? string.Empty, null, SegmentType.Current));
        }

        // Apply overflow logic
        var visiblePieces = ApplyOverflow(segments);
        foreach (var piece in visiblePieces)
        {
            Pieces.Add(piece);
        }
    }

    private static List<Note> BuildNoteChain(Note note, IReadOnlyCollection<Note> allNotes)
    {
        var chain = new List<Note> { note };
        var current = note;
        var visited = new HashSet<string> { note.NoteId };
        var notesById = allNotes.ToDictionary(n => n.NoteId);

        while (!string.IsNullOrEmpty(current.ParentNoteId))
        {
            if (visited.Contains(current.ParentNoteId))
                break; // Cycle detection

            if (!notesById.TryGetValue(current.ParentNoteId, out var parent))
                break;

            visited.Add(parent.NoteId);
            chain.Insert(0, parent);
            current = parent;
        }

        return chain;
    }

    private static List<string> GetFolderPath(NoteFolder folder, IReadOnlyDictionary<string, NoteFolder> foldersById)
    {
        var path = new List<string> { folder.Name };
        var current = folder;
        while (!string.IsNullOrEmpty(current.ParentId) && foldersById.TryGetValue(current.ParentId, out var parent))
        {
            path.Insert(0, parent.Name);
            current = parent;
        }
        return path;
    }

    private List<NoteBreadcrumbPieceBase> ApplyOverflow(List<Segment> segments)
    {
        var result = new List<NoteBreadcrumbPieceBase>();

        if (segments.Count <= MaxVisibleItems)
        {
            // No overflow needed, show all
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                result.Add(CreateCrumb(
                    seg.Text,
                    seg.NoteId,
                    seg.Type == SegmentType.Current,
                    i > 0));
            }
            return result;
        }

        // Overflow needed: show first, ellipsis with hidden, parent (if exists), current
        // Always show first segment
        result.Add(CreateCrumb(segments[0].Text, segments[0].NoteId, false, false));

        // Determine what goes in ellipsis and what's visible
        // Rule: Always show first, current, and usually parent of current if there is one
        int lastVisibleIndex = segments.Count - 1; // Current
        int parentIndex = segments.Count - 2; // Parent of current (if exists)

        // Hidden items are everything between first and the visible items at the end
        int ellipsisStart = 1;
        int ellipsisEnd = parentIndex > 0 ? parentIndex : lastVisibleIndex;

        if (parentIndex > 0 && segments.Count > MaxVisibleItems)
        {
            // Show: First, Ellipsis, Parent, Current
            var hiddenItems = new List<NoteBreadcrumbHiddenItemVm>();
            for (int i = ellipsisStart; i < ellipsisEnd; i++)
            {
                hiddenItems.Add(new NoteBreadcrumbHiddenItemVm(
                    segments[i].Text,
                    segments[i].NoteId,
                    _navigateToNote));
            }

            if (hiddenItems.Count > 0)
            {
                result.Add(new NoteBreadcrumbEllipsisPieceVm(hiddenItems)
                {
                    ShowLeadingSeparator = true
                });
            }

            // Parent
            result.Add(CreateCrumb(segments[parentIndex].Text, segments[parentIndex].NoteId, false, true));
        }
        else if (segments.Count > MaxVisibleItems)
        {
            // Not enough room for parent, show: First, Ellipsis, Current
            var hiddenItems = new List<NoteBreadcrumbHiddenItemVm>();
            for (int i = ellipsisStart; i < lastVisibleIndex; i++)
            {
                hiddenItems.Add(new NoteBreadcrumbHiddenItemVm(
                    segments[i].Text,
                    segments[i].NoteId,
                    _navigateToNote));
            }

            if (hiddenItems.Count > 0)
            {
                result.Add(new NoteBreadcrumbEllipsisPieceVm(hiddenItems)
                {
                    ShowLeadingSeparator = true
                });
            }
        }

        // Current (always last)
        result.Add(CreateCrumb(segments[lastVisibleIndex].Text, null, true, true));

        return result;
    }

    private NoteBreadcrumbCrumbPieceVm CreateCrumb(string text, string? noteId, bool isCurrent, bool showLeadingSeparator)
    {
        var crumb = new NoteBreadcrumbCrumbPieceVm
        {
            Text = NoteBreadcrumbTitleFormatter.ToDisplayText(text),
            ToolTipTip = NoteBreadcrumbTitleFormatter.ToolTipFor(text),
            NoteId = noteId,
            IsCurrent = isCurrent,
            ShowLeadingSeparator = showLeadingSeparator
        };
        crumb.OnNavigate = _navigateToNote;
        return crumb;
    }

    private record Segment(string Text, string? NoteId, SegmentType Type);

    private enum SegmentType
    {
        Folder,
        Note,
        Current
    }
}
