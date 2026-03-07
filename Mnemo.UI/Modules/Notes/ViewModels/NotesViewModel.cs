using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Notes.ViewModels;

public partial class NotesViewModel : ViewModelBase, INavigationAware
{
    private const string LastOpenNoteIdKey = "Notes.LastOpenNoteId";
    private const string NotesSidebarOpenKey = "Notes.SidebarOpen";

    private readonly INoteService _noteService;
    private readonly INoteFolderService _folderService;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localizationService;

    [ObservableProperty]
    private Note? _selectedNote;

    /// <summary>
    /// Last selected tree item that was a note (used to keep selection when clicking folders).
    /// </summary>
    private NoteTreeItemViewModel? _lastSelectedNoteTreeItem;

    /// <summary>
    /// Guard flag: suppresses SelectedNote changes caused by collection rebuilds (e.g. RefreshFavouriteNotes).
    /// </summary>
    private bool _isRefreshingCollections;

    [ObservableProperty]
    private NoteTreeItemViewModel? _selectedTreeItem;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _breadcrumbText = string.Empty;

    [ObservableProperty]
    private string _createdText = string.Empty;

    [ObservableProperty]
    private string _modifiedText = string.Empty;

    /// <summary>
    /// Whether the current note is a favorite (for binding star icon; mirrors SelectedNote.IsFavorite).
    /// </summary>
    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>
    /// Root items for the sidebar tree (folders and root-level notes).
    /// </summary>
    public ObservableCollection<NoteTreeItemViewModel> RootTreeItems { get; } = new();

    /// <summary>
    /// Flat list of all notes (for backward compatibility and when tree is not used).
    /// </summary>
    public ObservableCollection<Note> Notes { get; } = new();

    /// <summary>
    /// Favourite notes shown in the sidebar.
    /// </summary>
    public ObservableCollection<NoteTreeItemViewModel> FavouriteNotes { get; } = new();

    /// <summary>
    /// Flat list of all notes for the "My Notes" sidebar section (ensures every note is visible).
    /// </summary>
    public ObservableCollection<NoteTreeItemViewModel> AllNotesTreeItems { get; } = new();

    /// <summary>
    /// Flattened list of all tree items (folders + notes at all levels) for ListBox display.
    /// </summary>
    public ObservableCollection<NoteTreeItemViewModel> FlattenedTreeItems { get; } = new();

    /// <summary>
    /// Number of deleted notes (placeholder until soft-delete is implemented).
    /// </summary>
    [ObservableProperty]
    private int _deletedCount;

    private Dictionary<string, NoteFolder> _foldersById = new();

    public NotesViewModel(INoteService noteService, INoteFolderService folderService, ISettingsService settingsService, ILocalizationService localizationService)
    {
        _noteService = noteService;
        _folderService = folderService;
        _settingsService = settingsService;
        _localizationService = localizationService;

        _settingsService.SettingChanged += OnSettingChanged;
    }

    private void OnSettingChanged(object? sender, string key)
    {
        if (key == "Editor.Width")
        {
            _ = UpdateEditorWidthAsync();
        }
    }

    [RelayCommand]
    private async Task LoadNotesAsync()
    {
        IsLoading = true;
        try
        {
            await UpdateEditorWidthAsync();
            var folders = (await _folderService.GetAllFoldersAsync()).ToList();
            var notes = (await _noteService.GetAllNotesAsync()).ToList();

            _foldersById = folders.ToDictionary(f => f.FolderId);

            Notes.Clear();
            foreach (var n in notes)
                Notes.Add(n);

            BuildTree(folders, notes);
            RefreshAllNotesFlatList(notes);
            RefreshFavouriteNotes();

            // Restore last open note if it still exists (find item in tree so TreeView highlights it)
            var lastNoteId = await _settingsService.GetAsync<string?>(LastOpenNoteIdKey, null);
            if (!string.IsNullOrEmpty(lastNoteId))
            {
                var matchingItem = FindTreeItemByNoteId(RootTreeItems, lastNoteId);
                if (matchingItem == null)
                    matchingItem = FavouriteNotes.FirstOrDefault(i => i.Note?.NoteId == lastNoteId);
                if (matchingItem != null)
                    SelectedTreeItem = matchingItem;
            }

            var sidebarOpen = await _settingsService.GetAsync(NotesSidebarOpenKey, true);
            _sidebarStateLoaded = false;
            IsSidebarOpen = sidebarOpen;
            _sidebarStateLoaded = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RefreshAllNotesFlatList(List<Note>? notes = null)
    {
        var list = notes ?? Notes.ToList();
        AllNotesTreeItems.Clear();
        foreach (var note in list.OrderByDescending(n => n.ModifiedAt))
            AllNotesTreeItems.Add(new NoteTreeItemViewModel(note));
    }

    private void RefreshFlattenedTreeItems()
    {
        FlattenedTreeItems.Clear();
        foreach (var item in RootTreeItems)
            FlattenRecursive(item, FlattenedTreeItems);
    }

    private static void FlattenRecursive(NoteTreeItemViewModel item, ObservableCollection<NoteTreeItemViewModel> target)
    {
        target.Add(item);
        foreach (var child in item.Children)
            FlattenRecursive(child, target);
    }

    private void RefreshFavouriteNotes()
    {
        _isRefreshingCollections = true;
        try
        {
            FavouriteNotes.Clear();
            foreach (var note in Notes.Where(n => n.IsFavorite).OrderByDescending(n => n.ModifiedAt))
                FavouriteNotes.Add(new NoteTreeItemViewModel(note));
        }
        finally
        {
            _isRefreshingCollections = false;
        }
    }

    private void BuildTree(List<NoteFolder> folders, List<Note> notes)
    {
        var expandedFolderIds = CollectExpandedFolderIds(RootTreeItems);
        RootTreeItems.Clear();

        var folderIds = new HashSet<string>(folders.Select(f => f.FolderId));
        var rootFolders = folders
            .Where(f => string.IsNullOrEmpty(f.ParentId))
            .OrderBy(f => f.Order).ThenBy(f => f.Name)
            .ToList();
        var rootNotes = notes
            .Where(n => string.IsNullOrEmpty(n.FolderId))
            .OrderBy(n => n.Order)
            .ThenByDescending(n => n.ModifiedAt)
            .ToList();
        var orphanNotes = notes
            .Where(n => !string.IsNullOrEmpty(n.FolderId) && !folderIds.Contains(n.FolderId))
            .OrderByDescending(n => n.ModifiedAt)
            .ToList();

        // Folders first (with their nested children)
        foreach (var f in rootFolders)
        {
            var node = new NoteTreeItemViewModel(f);
            AddChildren(node, f.FolderId, folders, notes);
            RootTreeItems.Add(node);
        }

        // Notes without a folder appear directly at root level (no synthetic wrapper needed)
        foreach (var n in rootNotes.Concat(orphanNotes).OrderBy(n => n.Order).ThenByDescending(n => n.ModifiedAt))
            RootTreeItems.Add(new NoteTreeItemViewModel(n));

        RestoreExpanded(RootTreeItems, expandedFolderIds);
    }

    private static HashSet<string?> CollectExpandedFolderIds(ObservableCollection<NoteTreeItemViewModel> root)
    {
        var set = new HashSet<string?>();
        foreach (var item in root)
            CollectExpandedRecursive(item, set);
        return set;
    }

    private static void CollectExpandedRecursive(NoteTreeItemViewModel node, HashSet<string?> set)
    {
        if (node.IsFolder && node.IsExpanded && node.FolderId != null)
            set.Add(node.FolderId);
        foreach (var child in node.Children)
            CollectExpandedRecursive(child, set);
    }

    private static void RestoreExpanded(ObservableCollection<NoteTreeItemViewModel> root, HashSet<string?> expandedIds)
    {
        foreach (var item in root)
        {
            if (item.IsFolder && item.FolderId != null && expandedIds.Contains(item.FolderId))
                item.IsExpanded = true;
            RestoreExpanded(item.Children, expandedIds);
        }
    }

    private void AddChildren(NoteTreeItemViewModel node, string parentFolderId, List<NoteFolder> folders, List<Note> notes)
    {
        var childFolders = folders.Where(f => f.ParentId == parentFolderId).OrderBy(f => f.Order).ThenBy(f => f.Name).ToList();
        var childNotes = notes.Where(n => n.FolderId == parentFolderId).OrderBy(n => n.Order).ThenByDescending(n => n.ModifiedAt).ToList();

        foreach (var f in childFolders)
        {
            var childNode = new NoteTreeItemViewModel(f);
            AddChildren(childNode, f.FolderId, folders, notes);
            node.Children.Add(childNode);
        }

        foreach (var n in childNotes)
            node.Children.Add(new NoteTreeItemViewModel(n));
    }

    partial void OnSelectedTreeItemChanged(NoteTreeItemViewModel? value)
    {
        // Folders are not selectable: if TreeView selected a folder, revert to last selected note.
        if (value != null && value.IsFolder)
        {
            SelectedTreeItem = _lastSelectedNoteTreeItem;
            return;
        }

        // Ignore null changes caused by collection rebuilds (e.g. RefreshFavouriteNotes clearing TreeView)
        if (value == null && _isRefreshingCollections)
            return;

        if (value == null)
        {
            _lastSelectedNoteTreeItem = null;
            SelectedNote = null;
        }
        else if (value.Note != null)
        {
            _lastSelectedNoteTreeItem = value;
            SelectedNote = value.Note;
        }
        else
        {
            SelectedNote = null;
        }
    }

    partial void OnSelectedNoteChanged(Note? value)
    {
        if (value == null)
        {
            BreadcrumbText = string.Empty;
            CreatedText = string.Empty;
            ModifiedText = string.Empty;
            IsFavorite = false;
            return;
        }

        _ = _settingsService.SetAsync(LastOpenNoteIdKey, value.NoteId);

        IsFavorite = value.IsFavorite;

        if (!string.IsNullOrEmpty(value.FolderPath))
        {
            BreadcrumbText = value.FolderPath.TrimEnd('/').TrimEnd().Replace("/", " / ") + " / " + value.Title;
        }
        else if (value.FolderId != null && _foldersById.TryGetValue(value.FolderId, out var folder))
        {
            var path = GetFolderPath(folder);
            BreadcrumbText = path.Count > 0 ? string.Join(" / ", path) + " / " + value.Title : value.Title;
        }
        else
        {
            BreadcrumbText = value.Title;
        }

        CreatedText = FormatRelative(value.CreatedAt, "Created", "Notes");
        ModifiedText = FormatRelative(value.ModifiedAt, "LastModified", "Notes");
    }

    private void RefreshBreadcrumbText()
    {
        if (SelectedNote == null) return;
        var value = SelectedNote;
        if (!string.IsNullOrEmpty(value.FolderPath))
            BreadcrumbText = value.FolderPath.TrimEnd('/').TrimEnd().Replace("/", " / ") + " / " + value.Title;
        else if (value.FolderId != null && _foldersById.TryGetValue(value.FolderId, out var folder))
        {
            var path = GetFolderPath(folder);
            BreadcrumbText = path.Count > 0 ? string.Join(" / ", path) + " / " + value.Title : value.Title;
        }
        else
            BreadcrumbText = value.Title;
        OnPropertyChanged(nameof(BreadcrumbText));
    }

    private List<string> GetFolderPath(NoteFolder folder)
    {
        var path = new List<string> { folder.Name };
        var current = folder;
        while (!string.IsNullOrEmpty(current.ParentId) && _foldersById.TryGetValue(current.ParentId, out var parent))
        {
            path.Insert(0, parent.Name);
            current = parent;
        }
        return path;
    }

    /// <summary>
    /// Persists the current note's blocks and title. Call from view when block editor or title changes.
    /// </summary>
    public async Task SaveCurrentNoteAsync(Block[]? blocks, string? title = null)
    {
        if (SelectedNote == null) return;
        await SaveNoteWithContentAsync(SelectedNote, blocks, title);
    }

    /// <summary>
    /// Persists a specific note's blocks/title. Used when flushing pending save after note switch (editor still has previous note's content).
    /// </summary>
    public async Task SaveNoteWithContentAsync(Note note, Block[]? blocks, string? title = null)
    {
        if (note == null) return;

        if (title != null)
        {
            note.Title = title;
            NotifyTreeItemsForNoteTitleChanged(note);
            if (SelectedNote == note)
                RefreshBreadcrumbText();
        }

        if (blocks != null)
        {
            note.Blocks = blocks.Length > 0 ? blocks.ToList() : new List<Block>();
            note.Content = "";
        }

        await _noteService.SaveNoteAsync(note);
        if (SelectedNote == note)
            ModifiedText = FormatRelative(note.ModifiedAt, "LastModified", "Notes");
    }

    /// <summary>
    /// Notifies all tree items that wrap the given note so the sidebar updates the displayed title.
    /// </summary>
    private void NotifyTreeItemsForNoteTitleChanged(Note note)
    {
        foreach (var item in FlattenedTreeItems.Where(i => i.Note == note))
            item.NotifyNameChanged();
        foreach (var item in FavouriteNotes.Where(i => i.Note == note))
            item.NotifyNameChanged();
        foreach (var item in AllNotesTreeItems.Where(i => i.Note == note))
            item.NotifyNameChanged();
    }

    /// <summary>
    /// Returns blocks for the current note: either Note.Blocks or a single text block from Content.
    /// </summary>
    public Block[] GetBlocksForCurrentNote()
    {
        if (SelectedNote == null) return Array.Empty<Block>();

        if (SelectedNote.Blocks != null && SelectedNote.Blocks.Count > 0)
        {
            var ordered = SelectedNote.Blocks.OrderBy(b => b.Order).ToArray();
            for (var i = 0; i < ordered.Length; i++)
                ordered[i].Order = i;
            return ordered;
        }

        return new[]
        {
            new Block
            {
                Id = Guid.NewGuid().ToString(),
                Type = BlockType.Text,
                Content = SelectedNote.Content ?? "",
                Order = 0
            }
        };
    }

    private bool _sidebarStateLoaded = false;

    [ObservableProperty]
    private bool _isSidebarOpen = false;

    partial void OnIsSidebarOpenChanged(bool value)
    {
        if (_sidebarStateLoaded)
            _ = _settingsService.SetAsync(NotesSidebarOpenKey, value);
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarOpen = !IsSidebarOpen;
    }

    [RelayCommand]
    private async Task NewNoteAsync()
    {
        var note = new Note { Title = "Untitled", FolderId = null };

        Notes.Add(note);
        var item = new NoteTreeItemViewModel(note);

        // Insert after all root-level folders so notes group below folders, newest first
        var insertIndex = RootTreeItems.TakeWhile(i => i.IsFolder).Count();
        RootTreeItems.Insert(insertIndex, item);
        AllNotesTreeItems.Insert(0, item);
        RefreshFlattenedTreeItems();
        SelectedTreeItem = item;
        SelectedNote = note;

        _ = await _noteService.SaveNoteAsync(note);
    }

    // Search and Recent commands removed as per request


    [RelayCommand]
    private void SelectFavourite(NoteTreeItemViewModel? item)
    {
        if (item != null)
        {
            SelectedTreeItem = item;
            if (item.Note != null)
                SelectedNote = item.Note;
        }
    }

    [RelayCommand]
    private async Task DeleteNoteAsync(NoteTreeItemViewModel? item)
    {
        if (item?.Note == null) return;

        var note = item.Note;
        var result = await _noteService.DeleteNoteAsync(note.NoteId);
        if (!result.IsSuccess) return;

        if (SelectedNote == note)
        {
            SelectedTreeItem = null;
            SelectedNote = null;
        }

        Notes.Remove(note);
        RemoveNoteTreeItem(AllNotesTreeItems, item);
        RemoveNoteTreeItem(FavouriteNotes, item);
        RemoveNoteTreeItemFromRoot(RootTreeItems, item);
        RefreshFlattenedTreeItems();
    }

    [RelayCommand]
    private async Task DeleteFolderAsync(NoteTreeItemViewModel? item)
    {
        if (item?.Folder == null || item.FolderId == null) return;

        var folderId = item.FolderId;
        var clearSelection = SelectedTreeItem != null &&
            (ReferenceEquals(SelectedTreeItem, item) || SelectedTreeItem.Note?.FolderId == folderId);
        // Move notes in this folder to root
        foreach (var note in Notes.Where(n => n.FolderId == folderId).ToList())
        {
            note.FolderId = null;
            await _noteService.SaveNoteAsync(note);
        }
        // Move child folders to root
        foreach (var folder in _foldersById.Values.Where(f => f.ParentId == folderId).ToList())
        {
            folder.ParentId = null;
            await _folderService.SaveFolderAsync(folder);
        }
        var result = await _folderService.DeleteFolderAsync(folderId);
        if (!result.IsSuccess) return;

        _foldersById.Remove(folderId);
        if (clearSelection)
        {
            SelectedTreeItem = null;
            SelectedNote = null;
        }
        var foldersList = _foldersById.Values.ToList();
        var notesList = Notes.ToList();
        BuildTree(foldersList, notesList);
        RefreshFlattenedTreeItems();
        RefreshFavouriteNotes();
    }

    [RelayCommand]
    private async Task RenameFolderAsync(NoteTreeItemViewModel? item)
    {
        if (item?.Folder == null) return;
        await _folderService.SaveFolderAsync(item.Folder);
    }

    private static void RemoveNoteTreeItem(ObservableCollection<NoteTreeItemViewModel> collection, NoteTreeItemViewModel item)
    {
        for (var i = 0; i < collection.Count; i++)
        {
            if (ReferenceEquals(collection[i], item))
            {
                collection.RemoveAt(i);
                return;
            }
        }
    }

    private void RemoveNoteTreeItemFromRoot(ObservableCollection<NoteTreeItemViewModel> rootItems, NoteTreeItemViewModel item)
    {
        for (var i = 0; i < rootItems.Count; i++)
        {
            if (ReferenceEquals(rootItems[i], item))
            {
                rootItems.RemoveAt(i);
                return;
            }
            if (RemoveNoteTreeItemRecursive(rootItems[i].Children, item))
                return;
        }
    }

    private static bool RemoveNoteTreeItemRecursive(ObservableCollection<NoteTreeItemViewModel> children, NoteTreeItemViewModel item)
    {
        for (var i = 0; i < children.Count; i++)
        {
            if (ReferenceEquals(children[i], item))
            {
                children.RemoveAt(i);
                return true;
            }
            if (RemoveNoteTreeItemRecursive(children[i].Children, item))
                return true;
        }
        return false;
    }

    private static NoteTreeItemViewModel? FindTreeItemByNoteId(IEnumerable<NoteTreeItemViewModel> items, string noteId)
    {
        foreach (var item in items)
        {
            if (item.Note?.NoteId == noteId)
                return item;
            var found = FindTreeItemByNoteId(item.Children, noteId);
            if (found != null)
                return found;
        }
        return null;
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (SelectedNote == null) return;

        SelectedNote.IsFavorite = !SelectedNote.IsFavorite;
        IsFavorite = SelectedNote.IsFavorite;
        await _noteService.SaveNoteAsync(SelectedNote);
        RefreshFavouriteNotes();
    }

    [RelayCommand]
    private async Task NewFolderAsync()
    {
        var folder = new NoteFolder { Name = "New folder", ParentId = null };
        var result = await _folderService.SaveFolderAsync(folder);
        if (!result.IsSuccess) return;

        _foldersById[folder.FolderId] = folder;
        var folders = _foldersById.Values.ToList();
        var notes = Notes.ToList();
        BuildTree(folders, notes);
        RefreshFlattenedTreeItems();
    }

    /// <summary>
    /// Data key for drag-drop of tree items.
    /// </summary>
    public const string NoteTreeItemDragKey = "NoteTreeItemViewModel";

    /// <summary>
    /// Moves a tree item to root (parentless). Call when user drops on "My Notes" section background or header.
    /// </summary>
    public async Task MoveTreeItemToRootAsync(NoteTreeItemViewModel source)
    {
        if (source == null) return;

        if (source.IsFolder && source.Folder != null)
        {
            source.Folder.ParentId = null;
            var r = await _folderService.SaveFolderAsync(source.Folder);
            if (!r.IsSuccess) return;
        }
        else if (source.Note != null)
        {
            source.Note.FolderId = null;
            var r = await _noteService.SaveNoteAsync(source.Note);
            if (!r.IsSuccess) return;
        }
        else
            return;

        var foldersList = _foldersById.Values.ToList();
        var notesForTree = Notes.ToList();
        BuildTree(foldersList, notesForTree);
        RefreshFlattenedTreeItems();
        RefreshFavouriteNotes();
    }

    /// <summary>
    /// Handles dropping a tree item: either move into a folder (dropOnFolder) or reorder among siblings (insert after target).
    /// </summary>
    public async Task MoveTreeItemAsync(NoteTreeItemViewModel source, NoteTreeItemViewModel target, bool dropOnFolder, bool insertAfterTarget)
    {
        if (source == null || target == null) return;
        if (ReferenceEquals(source, target) && dropOnFolder) return;

        if (dropOnFolder)
        {
            if (!target.IsFolder || target.FolderId == null) return; // synthetic "Uncategorized" etc. have no FolderId
            if (source.IsFolder && IsDescendantOf(target.FolderId!, source.FolderId!)) return; // prevent moving folder into its own descendant

            if (source.IsFolder && source.Folder != null)
            {
                source.Folder.ParentId = target.FolderId;
                var siblingFolders = _foldersById.Values.Where(f => f.ParentId == target.FolderId).OrderBy(f => f.Order).ToList();
                var newIndex = siblingFolders.Count;
                source.Folder.Order = newIndex;
                var r = await _folderService.SaveFolderAsync(source.Folder);
                if (!r.IsSuccess) return;
            }
            else if (source.Note != null)
            {
                source.Note.FolderId = target.FolderId;
                var siblingNotes = Notes.Where(n => n.FolderId == target.FolderId).OrderBy(n => n.Order).ToList();
                source.Note.Order = siblingNotes.Count;
                var r = await _noteService.SaveNoteAsync(source.Note);
                if (!r.IsSuccess) return;
            }
            else
                return;

            var foldersList = _foldersById.Values.ToList();
            var notesForTree = Notes.ToList();
            BuildTree(foldersList, notesForTree);
            RefreshFlattenedTreeItems();
            RefreshFavouriteNotes();
            return;
        }

        // Reorder or move to different parent (e.g. drag out of folder to root)
        var (sourceCol, sourceIndex) = FindContainingCollection(RootTreeItems, source);
        var (targetCol, targetIndex) = FindContainingCollection(RootTreeItems, target);
        if (sourceCol == null || targetCol == null) return;

        var fromIdx = sourceIndex;
        var toIdx = insertAfterTarget ? targetIndex + 1 : targetIndex;
        if (fromIdx < 0 || toIdx < 0) return;

        var sameParent = ReferenceEquals(sourceCol, targetCol);
        var insertIdx = sameParent && toIdx > fromIdx ? toIdx - 1 : toIdx;
        if (insertIdx < 0) insertIdx = 0;

        var item = sourceCol[fromIdx];
        sourceCol.RemoveAt(fromIdx);

        if (!sameParent)
        {
            // Moving to a different parent (e.g. drag out to root or into another folder's list)
            var targetParentFolderId = GetParentFolderIdForCollection(RootTreeItems, targetCol);
            if (item.IsFolder && item.Folder != null)
            {
                if (targetParentFolderId != null && IsDescendantOf(targetParentFolderId, item.FolderId!))
                    { sourceCol.Insert(fromIdx, item); return; } // prevent folder into its own descendant
                item.Folder.ParentId = targetParentFolderId;
                var r = await _folderService.SaveFolderAsync(item.Folder);
                if (!r.IsSuccess) { sourceCol.Insert(fromIdx, item); return; }
            }
            else if (item.Note != null)
            {
                item.Note.FolderId = targetParentFolderId;
                var r = await _noteService.SaveNoteAsync(item.Note);
                if (!r.IsSuccess) { sourceCol.Insert(fromIdx, item); return; }
            }
            else
            {
                sourceCol.Insert(fromIdx, item);
                return;
            }
        }
        else if (fromIdx == toIdx || (fromIdx < toIdx && toIdx == fromIdx + 1))
        {
            sourceCol.Insert(fromIdx, item);
            return;
        }

        targetCol.Insert(Math.Min(insertIdx, targetCol.Count), item);
        await PersistOrderAsync(sourceCol);
        await PersistOrderAsync(targetCol);
        var folders = _foldersById.Values.ToList();
        var notesList = Notes.ToList();
        BuildTree(folders, notesList);
        RefreshFlattenedTreeItems();
        RefreshFavouriteNotes();
    }

    private bool IsDescendantOf(string folderId, string potentialAncestorId)
    {
        var current = folderId;
        while (!string.IsNullOrEmpty(current) && _foldersById.TryGetValue(current, out var folder))
        {
            if (folder.ParentId == potentialAncestorId) return true;
            current = folder.ParentId;
        }
        return false;
    }

    private static (ObservableCollection<NoteTreeItemViewModel>? collection, int index) FindContainingCollection(
        ObservableCollection<NoteTreeItemViewModel> root,
        NoteTreeItemViewModel item)
    {
        for (var i = 0; i < root.Count; i++)
        {
            if (ReferenceEquals(root[i], item))
                return (root, i);
            var (col, idx) = FindContainingCollection(root[i].Children, item);
            if (col != null) return (col, idx);
        }
        return (null, -1);
    }

    /// <summary>
    /// Returns the folder id that contains the given collection (null for RootTreeItems).
    /// </summary>
    private static string? GetParentFolderIdForCollection(
        ObservableCollection<NoteTreeItemViewModel> root,
        ObservableCollection<NoteTreeItemViewModel> collection)
    {
        if (ReferenceEquals(collection, root)) return null;
        for (var i = 0; i < root.Count; i++)
        {
            if (ReferenceEquals(root[i].Children, collection))
                return root[i].FolderId;
            var found = GetParentFolderIdForCollection(root[i].Children, collection);
            if (found != null) return found;
        }
        return null;
    }

    private async Task PersistOrderAsync(ObservableCollection<NoteTreeItemViewModel> siblings)
    {
        for (var i = 0; i < siblings.Count; i++)
        {
            var node = siblings[i];
            if (node.Folder != null)
            {
                node.Folder.Order = i;
                await _folderService.SaveFolderAsync(node.Folder);
            }
            else if (node.Note != null)
            {
                node.Note.Order = i;
                await _noteService.SaveNoteAsync(node.Note);
            }
        }
    }

    [ObservableProperty]
    private double _editorMaxWidth = 1000;

    public void OnNavigatedTo(object? parameter)
    {
        _ = LoadNotesCommand.ExecuteAsync(null);
    }

    private async Task UpdateEditorWidthAsync()
    {
        var widthStr = await _settingsService.GetAsync("Editor.Width", _localizationService.T("Wide", "Settings"));
        if (string.IsNullOrWhiteSpace(widthStr))
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => EditorMaxWidth = 1000);
            return;
        }
        var superCompact = _localizationService.T("SuperCompact", "Settings");
        var compact = _localizationService.T("Compact", "Settings");
        var wide = _localizationService.T("Wide", "Settings");
        var superWide = _localizationService.T("SuperWide", "Settings");

        double width = 1000;
        if (widthStr == superCompact) width = 600;
        else if (widthStr == compact) width = 800;
        else if (widthStr == wide) width = 1000;
        else if (widthStr == superWide) width = 1600;

        var w = width;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => EditorMaxWidth = w);
    }

    private string FormatRelative(DateTime dateTime, string prefixKey, string ns)
    {
        var prefix = _localizationService.T(prefixKey, ns);
        var utc = dateTime.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc) : dateTime.ToUniversalTime();
        var diff = DateTime.UtcNow - utc;

        if (diff.TotalMinutes < 1) return $"{prefix} {_localizationService.T("JustNow", ns)}";
        if (diff.TotalMinutes < 60) return $"{prefix} {string.Format(_localizationService.T("MinutesAgo", ns), (int)diff.TotalMinutes)}";
        if (diff.TotalHours < 24) return $"{prefix} {string.Format(_localizationService.T("HoursAgo", ns), (int)diff.TotalHours)}";
        if (diff.TotalDays < 7) return $"{prefix} {string.Format(_localizationService.T("DaysAgo", ns), (int)diff.TotalDays)}";
        if (diff.TotalDays < 30) return $"{prefix} {string.Format(_localizationService.T("WeeksAgo", ns), (int)(diff.TotalDays / 7))}";
        if (diff.TotalDays < 365) return $"{prefix} {string.Format(_localizationService.T("MonthsAgo", ns), (int)(diff.TotalDays / 30))}";
        return $"{prefix} {string.Format(_localizationService.T("YearsAgo", ns), (int)(diff.TotalDays / 365))}";
    }
}
