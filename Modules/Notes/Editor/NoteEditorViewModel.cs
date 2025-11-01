using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using MnemoApp.Core.Common;
using MnemoApp.Data.Runtime;
using MnemoApp.Modules.Notes.Models;
using MnemoApp.UI.Components.BlockEditor;

namespace MnemoApp.Modules.Notes.Editor;

public class NoteEditorViewModel : ViewModelBase
{
    private readonly IRuntimeStorage _storage;
    private readonly Timer _autoSaveTimer;

    private string _noteId = string.Empty;
    private string _title = string.Empty;
    private string _createdAt = string.Empty;
    private string[] _tags = Array.Empty<string>();
    private bool _hasUnsavedChanges;
    private bool _isLoading;

    public Action? OnClose { get; set; }

    public string NoteId
    {
        get => _noteId;
        set
        {
            if (SetProperty(ref _noteId, value) && !string.IsNullOrEmpty(value))
            {
                // If BlockEditorInstance is already available, load immediately
                if (BlockEditorInstance != null)
                {
                    _ = LoadNoteAsync(value);
                }
            }
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
            {
                HasUnsavedChanges = true;
                ResetAutoSaveTimer();
            }
        }
    }

    public string CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value);
    }

    public string[] Tags
    {
        get => _tags;
        set => SetProperty(ref _tags, value);
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set => SetProperty(ref _hasUnsavedChanges, value);
    }

    private BlockEditor? _blockEditorInstance;
    public BlockEditor? BlockEditorInstance
    {
        get => _blockEditorInstance;
        set
        {
            if (_blockEditorInstance == value)
                return;
                
            _blockEditorInstance = value;
            // If NoteId is already set when BlockEditorInstance is assigned, load the note
            if (value != null && !string.IsNullOrEmpty(_noteId) && !_isLoading)
            {
                // Use InvokeAsync to properly handle async operation
                _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await LoadNoteAsync(_noteId);
                }, Avalonia.Threading.DispatcherPriority.Loaded);
            }
        }
    }

    public NoteEditorViewModel(IRuntimeStorage storage)
    {
        _storage = storage;

        // Auto-save timer (500ms debounce)
        _autoSaveTimer = new Timer(500);
        _autoSaveTimer.Elapsed += async (s, e) => await SaveNoteAsync();
        _autoSaveTimer.AutoReset = false;
    }

    public async Task LoadNoteAsync(string noteId)
    {
        // Prevent concurrent loads
        if (_isLoading)
            return;

        _isLoading = true;
        // Set field directly to avoid triggering property setter (which would cause double-load)
        _noteId = noteId;
        OnPropertyChanged(nameof(NoteId));

        try
        {
            NoteData? note = null;
            await Task.Run(() =>
            {
                try
                {
                    note = _storage.GetProperty<NoteData>($"Content/Notes/{noteId}");
                }
                catch (System.Text.Json.JsonException)
                {
                    // Try loading as old format (JSON string)
                    var noteJson = _storage.GetProperty<string>($"Content/Notes/{noteId}");
                    if (noteJson != null)
                    {
                        note = System.Text.Json.JsonSerializer.Deserialize<NoteData>(noteJson);
                        if (note != null)
                        {
                            // Migrate to new format
                            _storage.SetProperty($"Content/Notes/{noteId}", note);
                        }
                    }
                }
            });

            if (note != null)
            {
                Title = note.Title;
                CreatedAt = note.CreatedAt.ToString("MMMM dd, yyyy hh:mm tt");
                Tags = note.Tags;

                // Load blocks into editor (we're already on UI thread after await)
                if (BlockEditorInstance != null)
                {
                    var blocks = note.Blocks ?? Array.Empty<Block>();
                    BlockEditorInstance.LoadBlocks(blocks);
                    
                    // Ensure at least one block exists (fallback if blocks array was empty)
                    if (BlockEditorInstance.Blocks.Count == 0)
                    {
                        BlockEditorInstance.AddBlock(BlockType.Text);
                    }
                }
                else
                {
                    // If BlockEditorInstance isn't set yet, wait for it
                    // The BlockEditorInstance setter will trigger loading
                }
                HasUnsavedChanges = false;
            }
            else
            {
                // If note not found, ensure we still have at least one block
                if (BlockEditorInstance != null)
                {
                    if (BlockEditorInstance.Blocks.Count == 0)
                    {
                        BlockEditorInstance.AddBlock(BlockType.Text);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // TODO: Show error notification
            Console.WriteLine($"Error loading note: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    public void OnBlocksChanged()
    {
        System.Diagnostics.Debug.WriteLine($"[NoteEditorViewModel] OnBlocksChanged called - setting HasUnsavedChanges and resetting timer");
        HasUnsavedChanges = true;
        ResetAutoSaveTimer();
    }

    private void ResetAutoSaveTimer()
    {
        System.Diagnostics.Debug.WriteLine("[NoteEditorViewModel] ResetAutoSaveTimer - stopping and starting timer");
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private async Task SaveNoteAsync()
    {
        System.Diagnostics.Debug.WriteLine($"[NoteEditorViewModel] SaveNoteAsync called - HasUnsavedChanges: {HasUnsavedChanges}, BlockEditorInstance: {BlockEditorInstance != null}");
        if (!HasUnsavedChanges || BlockEditorInstance == null)
            return;

        try
        {
            // Get blocks on UI thread before going to background thread
            var blocks = BlockEditorInstance.GetBlocks();
            
            await Task.Run(() =>
            {

                // Update title from first heading or text block
                var titleBlock = blocks.FirstOrDefault(b => 
                    b.Type == BlockType.Heading1 || 
                    b.Type == BlockType.Heading2 || 
                    b.Type == BlockType.Text);

                if (titleBlock != null && !string.IsNullOrWhiteSpace(titleBlock.Content))
                {
                    Title = titleBlock.Content.Length > 50 
                        ? titleBlock.Content.Substring(0, 50) + "..." 
                        : titleBlock.Content;
                }

                var note = new NoteData
                {
                    Id = NoteId,
                    Title = Title,
                    Blocks = blocks,
                    UpdatedAt = DateTime.Now,
                    Tags = Tags
                };

                // Load existing note to preserve other fields
                NoteData? existing = null;
                try
                {
                    existing = _storage.GetProperty<NoteData>($"Content/Notes/{NoteId}");
                }
                catch (System.Text.Json.JsonException)
                {
                    // Try loading as old format (JSON string)
                    var existingJson = _storage.GetProperty<string>($"Content/Notes/{NoteId}");
                    if (existingJson != null)
                    {
                        existing = System.Text.Json.JsonSerializer.Deserialize<NoteData>(existingJson);
                    }
                }

                if (existing != null)
                {
                    note.CreatedAt = existing.CreatedAt;
                }

                _storage.SetProperty($"Content/Notes/{NoteId}", note);

                HasUnsavedChanges = false;
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving note: {ex.Message}");
        }
    }

    public async Task DeleteNoteAsync()
    {
        try
        {
            // Stop auto-save timer and prevent further saves
            _autoSaveTimer.Stop();
            HasUnsavedChanges = false;
            
            await _storage.RemovePropertyAsync($"Content/Notes/{NoteId}");

            // Close editor
            OnClose?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting note: {ex.Message}");
        }
    }

    public void CloseEditor()
    {
        _autoSaveTimer.Stop();
        HasUnsavedChanges = false;
        OnClose?.Invoke();
    }
}

