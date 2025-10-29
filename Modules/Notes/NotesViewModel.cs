using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using MnemoApp.Core.Common;
using MnemoApp.Core.Navigation;
using MnemoApp.Modules.Notes.Models;
using MnemoApp.Data.Runtime;
using MnemoApp.Core.Overlays;

namespace MnemoApp.Modules.Notes;

public class NotesViewModel : ViewModelBase
{
    private readonly IRuntimeStorage _storage;
    private readonly INavigationService _navigationService;
    private readonly IOverlayService _overlayManager;

    private ObservableCollection<TreeNodeViewModel> _folderTree = new();
    private ObservableCollection<NoteItemViewModel> _notesList = new();
    private TreeNodeViewModel? _selectedFolder;
    private NoteItemViewModel? _selectedNote;
    private string _searchText = string.Empty;
    private Editor.NoteEditorViewModel? _editorViewModel;
    private bool _isNoteSelected;

    public ObservableCollection<TreeNodeViewModel> FolderTree
    {
        get => _folderTree;
        set => SetProperty(ref _folderTree, value);
    }

    public ObservableCollection<NoteItemViewModel> NotesList
    {
        get => _notesList;
        set => SetProperty(ref _notesList, value);
    }

    public TreeNodeViewModel? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetProperty(ref _selectedFolder, value))
            {
                LoadNotesForFolder(value?.FolderId ?? string.Empty);
            }
        }
    }

    public NoteItemViewModel? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (SetProperty(ref _selectedNote, value) && value != null)
            {
                OpenNote(value.NoteId);
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                FilterNotes();
            }
        }
    }

    public Editor.NoteEditorViewModel? EditorViewModel
    {
        get => _editorViewModel;
        set => SetProperty(ref _editorViewModel, value);
    }

    public bool IsNoteSelected
    {
        get => _isNoteSelected;
        set => SetProperty(ref _isNoteSelected, value);
    }

    public NotesViewModel(IRuntimeStorage storage, INavigationService navigationService, IOverlayService overlayManager)
    {
        _storage = storage;
        _navigationService = navigationService;
        _overlayManager = overlayManager;

        // Migrate from old list-based storage
        MigrateLegacyStorage();

        LoadFolders();
    }

    private void MigrateLegacyStorage()
    {
        try
        {
            // Remove legacy list keys that conflict with ListContent queries
            try
            {
                if (_storage.HasProperty("Content/Notes/list"))
                {
                    System.Diagnostics.Debug.WriteLine("Migrating: Removing legacy Content/Notes/list");
                    _storage.RemoveProperty("Content/Notes/list");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Migration note: {ex.Message}");
            }

            try
            {
                if (_storage.HasProperty("Content/Folders/list"))
                {
                    System.Diagnostics.Debug.WriteLine("Migrating: Removing legacy Content/Folders/list");
                    _storage.RemoveProperty("Content/Folders/list");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Migration note: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Migration warning: {ex.Message}");
        }
    }

    private void LoadFolders()
    {
        try
        {
            // Load folders using ListContent
            var contentItems = _storage.ListContent<FolderData>("Folders");
            var folders = contentItems.Select(item => item.Data).ToList();

            // Always build tree structure (even if folders is empty)
            BuildFolderTree(folders);

            // Select "All Notes" by default and load notes for it
            if (FolderTree.Count > 0)
            {
                SelectedFolder = FolderTree[0];
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading folders: {ex.Message}");
            
            // Initialize with default "All Notes" folder
            FolderTree.Clear();
            FolderTree.Add(new TreeNodeViewModel
            {
                Name = "All Notes",
                FolderId = string.Empty,
                IsExpanded = true
            });
            SelectedFolder = FolderTree[0];
        }
    }

    private void BuildFolderTree(System.Collections.Generic.List<FolderData> folders)
    {
        FolderTree.Clear();

        // Always add "All Notes" root - this should always be visible
        var allNotes = new TreeNodeViewModel
        {
            Name = "All Notes",
            FolderId = string.Empty,
            IsExpanded = true,
            IsFolder = false  // Root "All Notes" item is not a folder
        };
        FolderTree.Add(allNotes);

        // Add root folders under "All Notes"
        if (folders != null && folders.Count > 0)
        {
            var rootFolders = folders.Where(f => string.IsNullOrEmpty(f.ParentId)).OrderBy(f => f.Name);
            foreach (var folder in rootFolders)
            {
                var node = CreateTreeNode(folder, folders);
                allNotes.Children.Add(node);
            }
        }
    }

    private TreeNodeViewModel CreateTreeNode(FolderData folder, System.Collections.Generic.List<FolderData> allFolders)
    {
        var node = new TreeNodeViewModel
        {
            Name = folder.Name,
            FolderId = folder.Id,
            IsFolder = true
        };

        // Add child folders
        var children = allFolders.Where(f => f.ParentId == folder.Id).OrderBy(f => f.Name);
        foreach (var child in children)
        {
            node.Children.Add(CreateTreeNode(child, allFolders));
        }

        return node;
    }

    private void LoadNotesForFolder(string folderId)
    {
        try
        {
            var contentItems = _storage.ListContent<NoteData>("Notes");
            
            var notes = contentItems
                .Where(item => string.IsNullOrEmpty(folderId) || item.Data.FolderId == folderId)
                .Select(item => item.Data)
                .OrderByDescending(n => n.UpdatedAt);

            NotesList.Clear();
            foreach (var note in notes)
            {
                NotesList.Add(new NoteItemViewModel
                {
                    NoteId = note.Id,
                    Title = note.Title,
                    Preview = GetPreview(note),
                    CreatedAt = note.CreatedAt,
                    Tags = note.Tags
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading notes for folder {folderId}: {ex.Message}");
            NotesList.Clear();
        }
    }

    private string GetPreview(NoteData note)
    {
        if (note.Blocks == null || note.Blocks.Length == 0)
            return string.Empty;

        var firstTextBlock = note.Blocks.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.Content));
        return firstTextBlock?.Content?.Substring(0, Math.Min(100, firstTextBlock.Content.Length)) ?? string.Empty;
    }

    private void FilterNotes()
    {
        try
        {
            var contentItems = _storage.ListContent<NoteData>("Notes");
            var folderId = SelectedFolder?.FolderId ?? string.Empty;
            
            var notes = contentItems
                .Select(item => item.Data)
                .Where(note =>
                {
                    // Filter by folder
                    if (!string.IsNullOrEmpty(folderId) && note.FolderId != folderId)
                        return false;
                    
                    // Filter by search text
                    if (string.IsNullOrWhiteSpace(_searchText))
                        return true;
                    
                    var searchLower = _searchText.ToLowerInvariant();
                    var titleMatch = note.Title.ToLowerInvariant().Contains(searchLower);
                    var contentMatch = note.Blocks?.Any(b => 
                        b.Content?.ToLowerInvariant().Contains(searchLower) ?? false) ?? false;
                    var tagMatch = note.Tags?.Any(t => 
                        t.ToLowerInvariant().Contains(searchLower)) ?? false;
                    
                    return titleMatch || contentMatch || tagMatch;
                })
                .OrderByDescending(n => n.UpdatedAt);

            NotesList.Clear();
            foreach (var note in notes)
            {
                NotesList.Add(new NoteItemViewModel
                {
                    NoteId = note.Id,
                    Title = note.Title,
                    Preview = GetPreview(note),
                    CreatedAt = note.CreatedAt,
                    Tags = note.Tags
                });
            }
        }
        catch
        {
            NotesList.Clear();
        }
    }

    private void OpenNote(string noteId)
    {
        // Create inline editor
        var editorVm = new Editor.NoteEditorViewModel(_storage);
        editorVm.NoteId = noteId;
        editorVm.OnClose = CloseNote;
        EditorViewModel = editorVm;
        IsNoteSelected = true;
    }

    private void CloseNote()
    {
        EditorViewModel = null;
        IsNoteSelected = false;
        // Refresh the notes list
        if (SelectedFolder != null)
        {
            LoadNotesForFolder(SelectedFolder.FolderId ?? string.Empty);
        }
    }

    public void CreateNote()
    {
        try
        {
            var initialBlock = new Block 
            { 
                Id = Guid.NewGuid().ToString(), 
                Type = BlockType.Text, 
                Order = 0, 
                Content = "", 
                Meta = new Dictionary<string, object>() 
            };
            
            var note = new NoteData
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Untitled",
                FolderId = SelectedFolder?.FolderId ?? string.Empty,
                Blocks = new[] { initialBlock },
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Tags = Array.Empty<string>()
            };

            SaveNote(note);
            LoadNotesForFolder(note.FolderId);
            OpenNote(note.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error creating note: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    public async void CreateFolderPrompt()
    {
        try
        {
            var dialog = new UI.Components.Overlays.InputDialogOverlay
            {
                Title = "Create Folder",
                Placeholder = "Folder name",
                InputValue = "New Folder",
                ConfirmText = "Create",
                CancelText = "Cancel"
            };

            var (overlayId, resultTask) = _overlayManager.CreateOverlayWithTask<string?>(dialog);
            dialog.OnResult = result => _overlayManager.CloseOverlay(overlayId, result);

            var name = await resultTask;
            if (!string.IsNullOrWhiteSpace(name))
            {
                CreateFolder(name);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error showing folder dialog: {ex.Message}");
        }
    }

    private void CreateFolder(string name)
    {
        try
        {
            var folder = new FolderData
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                ParentId = SelectedFolder?.FolderId ?? string.Empty
            };

            _storage.SetProperty($"Content/Folders/{folder.Id}", folder);

            LoadFolders();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error creating folder: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    private void SaveNote(NoteData note)
    {
        try
        {
            _storage.SetProperty($"Content/Notes/{note.Id}", note);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving note: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
        }
    }
}

public class TreeNodeViewModel : ViewModelBase
{
    private string _name = string.Empty;
    private string? _folderId;
    private bool _isExpanded;
    private bool _isFolder;
    private ObservableCollection<TreeNodeViewModel> _children = new();

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string? FolderId
    {
        get => _folderId;
        set => SetProperty(ref _folderId, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsFolder
    {
        get => _isFolder;
        set => SetProperty(ref _isFolder, value);
    }

    public ObservableCollection<TreeNodeViewModel> Children
    {
        get => _children;
        set => SetProperty(ref _children, value);
    }
}

public class NoteItemViewModel : ViewModelBase
{
    private string _noteId = string.Empty;
    private string _title = string.Empty;
    private string _preview = string.Empty;
    private DateTime _createdAt;
    private string[] _tags = Array.Empty<string>();

    public string NoteId
    {
        get => _noteId;
        set => SetProperty(ref _noteId, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Preview
    {
        get => _preview;
        set => SetProperty(ref _preview, value);
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value);
    }

    public string[] Tags
    {
        get => _tags;
        set => SetProperty(ref _tags, value);
    }
}

