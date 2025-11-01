using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
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
    private readonly Timer _filterDebounceTimer;

    private ObservableCollection<NoteItemViewModel> _notesList = new();
    private NoteItemViewModel? _selectedNote;
    private string _searchText = string.Empty;
    private Editor.NoteEditorViewModel? _editorViewModel;
    private bool _isNoteSelected;
    private readonly Dictionary<string, string> _previewCache = new();

    public ObservableCollection<NoteItemViewModel> NotesList
    {
        get => _notesList;
        set => SetProperty(ref _notesList, value);
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
                // Debounce filter to avoid excessive queries
                _filterDebounceTimer.Stop();
                _filterDebounceTimer.Start();
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

        // Filter debounce timer (300ms delay)
        _filterDebounceTimer = new Timer(300);
        _filterDebounceTimer.Elapsed += (s, e) =>
        {
            _filterDebounceTimer.Stop();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => FilterNotesInternal());
        };
        _filterDebounceTimer.AutoReset = false;

        // Load notes asynchronously to avoid blocking UI (includes migration)
        _ = LoadNotesAsync();
    }

    private async Task LoadNotesAsync()
    {
        // Migrate from old list-based storage (async, non-blocking)
        await MigrateLegacyStorageAsync();
        
        // Load notes
        await LoadNotes();
    }

    private async Task MigrateLegacyStorageAsync()
    {
        try
        {
            await Task.Run(() =>
            {
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
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Migration warning: {ex.Message}");
        }
    }

    private async Task LoadNotes()
    {
        try
        {
            var contentItems = await Task.Run(() => _storage.ListContent<NoteData>("Notes"));
            
            var notes = contentItems
                .Select(item => item.Data)
                .OrderByDescending(n => n.UpdatedAt);

            // Materialize list to avoid multiple enumerations
            var notesList = notes.ToList();
            
            // Batch update to reduce UI refreshes
            var newList = new ObservableCollection<NoteItemViewModel>();
            foreach (var note in notesList)
            {
                newList.Add(new NoteItemViewModel
                {
                    NoteId = note.Id,
                    Title = note.Title,
                    Preview = GetPreview(note),
                    CreatedAt = note.CreatedAt,
                    Tags = note.Tags
                });
            }

            if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => NotesList = newList);
            }
            else
            {
                NotesList = newList;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading notes: {ex.Message}");
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                NotesList.Clear();
            }
            else
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => NotesList.Clear());
            }
        }
    }

    private string GetPreview(NoteData note)
    {
        // Use cache if available
        if (_previewCache.TryGetValue(note.Id, out var cachedPreview))
            return cachedPreview;

        if (note.Blocks == null || note.Blocks.Length == 0)
        {
            _previewCache[note.Id] = string.Empty;
            return string.Empty;
        }

        var firstTextBlock = note.Blocks.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.Content));
        var preview = firstTextBlock?.Content?.Substring(0, Math.Min(100, firstTextBlock.Content.Length)) ?? string.Empty;
        _previewCache[note.Id] = preview;
        return preview;
    }

    private async void FilterNotesInternal()
    {
        try
        {
            List<NoteData> notes = await Task.Run(() =>
            {
                try
                {
                    var contentItems = _storage.ListContent<NoteData>("Notes");
                    
                    return contentItems
                        .Select(item => item.Data)
                        .Where(note =>
                        {
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
                        .OrderByDescending(n => n.UpdatedAt)
                        .ToList();
                }
                catch
                {
                    return new List<NoteData>();
                }
            });

            // Batch update to reduce UI refreshes - back on UI thread now
            var newList = new ObservableCollection<NoteItemViewModel>();
            foreach (var note in notes)
            {
                newList.Add(new NoteItemViewModel
                {
                    NoteId = note.Id,
                    Title = note.Title,
                    Preview = GetPreview(note),
                    CreatedAt = note.CreatedAt,
                    Tags = note.Tags
                });
            }
            
            NotesList = newList;
        }
        catch
        {
            NotesList.Clear();
        }
    }
    
    private void FilterNotes()
    {
        FilterNotesInternal();
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

    private async void CloseNote()
    {
        EditorViewModel = null;
        IsNoteSelected = false;
        // Clear preview cache for the closed note to refresh preview
        if (_selectedNote != null)
        {
            _previewCache.Remove(_selectedNote.NoteId);
        }
        // Refresh the notes list
        await LoadNotes();
    }

    public async void CreateNote()
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
                Blocks = new[] { initialBlock },
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Tags = Array.Empty<string>()
            };

            await SaveNote(note);
            await LoadNotes();
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


    private async Task SaveNote(NoteData note)
    {
        try
        {
            await _storage.SetPropertyAsync($"Content/Notes/{note.Id}", note);
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

