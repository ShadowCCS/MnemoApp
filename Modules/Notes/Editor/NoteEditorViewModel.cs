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
        set => SetProperty(ref _noteId, value);
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

    public BlockEditor? BlockEditorInstance { get; set; }

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
        NoteId = noteId;

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

                // Load blocks into editor on UI thread
                BlockEditorInstance?.LoadBlocks(note.Blocks);
                HasUnsavedChanges = false;
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
        HasUnsavedChanges = true;
        ResetAutoSaveTimer();
    }

    private void ResetAutoSaveTimer()
    {
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private async Task SaveNoteAsync()
    {
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
                    note.FolderId = existing.FolderId;
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

    public void DeleteNote()
    {
        try
        {
            _storage.RemoveProperty($"Content/Notes/{NoteId}");

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
        OnClose?.Invoke();
    }
}

