using System.Collections.Generic;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Service for loading and persisting notes.
/// </summary>
public interface INoteService
{
    /// <summary>
    /// Gets all notes.
    /// </summary>
    Task<IEnumerable<Note>> GetAllNotesAsync();

    /// <summary>
    /// Gets a note by id.
    /// </summary>
    Task<Note?> GetNoteAsync(string noteId);

    /// <summary>
    /// Saves a note (create or update).
    /// </summary>
    Task<Result> SaveNoteAsync(Note note);

    /// <summary>
    /// Deletes a note by id.
    /// </summary>
    Task<Result> DeleteNoteAsync(string noteId);
}
