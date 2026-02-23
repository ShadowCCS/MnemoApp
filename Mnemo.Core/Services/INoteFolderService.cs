using System.Collections.Generic;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

/// <summary>
/// Service for loading and persisting note folders (tree structure).
/// </summary>
public interface INoteFolderService
{
    Task<IEnumerable<NoteFolder>> GetAllFoldersAsync();
    Task<NoteFolder?> GetFolderAsync(string folderId);
    Task<Result> SaveFolderAsync(NoteFolder folder);
    Task<Result> DeleteFolderAsync(string folderId);
}
