using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

public class NoteService : INoteService
{
    private readonly IStorageProvider _storage;
    private const string IndexKey = "notes_index";

    public NoteService(IStorageProvider storage)
    {
        _storage = storage;
    }

    public async Task<IEnumerable<Note>> GetAllNotesAsync()
    {
        var indexResult = await _storage.LoadAsync<List<string>>(IndexKey);
        if (!indexResult.IsSuccess || indexResult.Value == null)
            return Enumerable.Empty<Note>();

        var notes = new List<Note>();
        foreach (var id in indexResult.Value)
        {
            var noteResult = await _storage.LoadAsync<Note>($"note_{id}");
            if (noteResult.IsSuccess && noteResult.Value != null)
                notes.Add(noteResult.Value);
        }

        return notes.OrderByDescending(n => n.ModifiedAt);
    }

    public async Task<Note?> GetNoteAsync(string noteId)
    {
        var result = await _storage.LoadAsync<Note>($"note_{noteId}");
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<Result> SaveNoteAsync(Note note)
    {
        note.ModifiedAt = System.DateTime.UtcNow;
        if (note.CreatedAt == default)
            note.CreatedAt = note.ModifiedAt;

        var saveResult = await _storage.SaveAsync($"note_{note.NoteId}", note);
        if (!saveResult.IsSuccess) return saveResult;

        var indexResult = await _storage.LoadAsync<List<string>>(IndexKey);
        var index = indexResult.Value ?? new List<string>();

        if (!index.Contains(note.NoteId))
        {
            index.Add(note.NoteId);
            await _storage.SaveAsync(IndexKey, index);
        }

        return Result.Success();
    }

    public async Task<Result> DeleteNoteAsync(string noteId)
    {
        var deleteResult = await _storage.DeleteAsync($"note_{noteId}");
        if (!deleteResult.IsSuccess) return deleteResult;

        var indexResult = await _storage.LoadAsync<List<string>>(IndexKey);
        if (indexResult.IsSuccess && indexResult.Value != null && indexResult.Value.Remove(noteId))
            await _storage.SaveAsync(IndexKey, indexResult.Value);

        return Result.Success();
    }
}
