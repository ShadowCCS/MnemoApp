using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.Core.Models;

namespace Mnemo.UI.Modules.Notes.ViewModels;

/// <summary>
/// Represents either a folder, a note, or a synthetic container (e.g. "Uncategorized") in the sidebar tree.
/// </summary>
public partial class NoteTreeItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded = true;

    public bool IsFolder { get; }
    public string? FolderId { get; }
    public NoteFolder? Folder { get; }
    public Note? Note { get; }

    /// <summary>
    /// True when this item is a folder with no children (used for empty folder icon).
    /// </summary>
    public bool IsFolderEmpty => IsFolder && Children.Count == 0;

    /// <summary>
    /// True when this item is a folder with at least one child (used for filled folder icon).
    /// </summary>
    public bool IsFolderWithChildren => IsFolder && Children.Count > 0;

    /// <summary>
    /// True when this item is a real folder that can be renamed (not a synthetic container like "Uncategorized").
    /// </summary>
    public bool IsRenamableFolder => IsFolder && Folder != null;

    /// <summary>
    /// Display name for synthetic container nodes (e.g. "Uncategorized"); null for real folders/notes.
    /// </summary>
    private readonly string? _syntheticName;

    /// <summary>
    /// Display name (folder name, note title, or synthetic name).
    /// </summary>
    public string Name => _syntheticName ?? (IsFolder ? (Folder?.Name ?? "") : (Note?.Title ?? "Untitled"));

    public ObservableCollection<NoteTreeItemViewModel> Children { get; } = new();

    public NoteTreeItemViewModel(NoteFolder folder)
    {
        IsFolder = true;
        FolderId = folder.FolderId;
        Folder = folder;
        Children.CollectionChanged += OnChildrenChanged;
    }

    /// <summary>
    /// Synthetic container with no backing folder (e.g. "Uncategorized" for notes without a folder).
    /// </summary>
    public NoteTreeItemViewModel(string displayName)
    {
        IsFolder = true;
        FolderId = null;
        Folder = null;
        _syntheticName = displayName;
        Children.CollectionChanged += OnChildrenChanged;
    }

    public NoteTreeItemViewModel(Note note)
    {
        IsFolder = false;
        Note = note;
    }

    /// <summary>
    /// Updates the folder name and notifies the view. Only valid when <see cref="Folder"/> is not null.
    /// </summary>
    public void SetFolderName(string name)
    {
        if (Folder == null) return;
        Folder.Name = name;
        OnPropertyChanged(nameof(Name));
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsFolderEmpty));
        OnPropertyChanged(nameof(IsFolderWithChildren));
    }
}
