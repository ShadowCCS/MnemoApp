using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

public class NoteFolderService : INoteFolderService
{
    private readonly IStorageProvider _storage;
    private const string IndexKey = "note_folders_index";

    public NoteFolderService(IStorageProvider storage)
    {
        _storage = storage;
    }

    public async Task<IEnumerable<NoteFolder>> GetAllFoldersAsync()
    {
        var indexResult = await _storage.LoadAsync<List<string>>(IndexKey);
        if (!indexResult.IsSuccess || indexResult.Value == null)
            return Enumerable.Empty<NoteFolder>();

        var folders = new List<NoteFolder>();
        foreach (var id in indexResult.Value)
        {
            var folderResult = await _storage.LoadAsync<NoteFolder>($"note_folder_{id}");
            if (folderResult.IsSuccess && folderResult.Value != null)
                folders.Add(folderResult.Value);
        }

        return folders.OrderBy(f => f.Order).ThenBy(f => f.Name);
    }

    public async Task<NoteFolder?> GetFolderAsync(string folderId)
    {
        var result = await _storage.LoadAsync<NoteFolder>($"note_folder_{folderId}");
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<Result> SaveFolderAsync(NoteFolder folder)
    {
        var saveResult = await _storage.SaveAsync($"note_folder_{folder.FolderId}", folder);
        if (!saveResult.IsSuccess) return saveResult;

        var indexResult = await _storage.LoadAsync<List<string>>(IndexKey);
        var index = indexResult.Value ?? new List<string>();

        if (!index.Contains(folder.FolderId))
        {
            index.Add(folder.FolderId);
            await _storage.SaveAsync(IndexKey, index);
        }

        return Result.Success();
    }

    public async Task<Result> DeleteFolderAsync(string folderId)
    {
        var deleteResult = await _storage.DeleteAsync($"note_folder_{folderId}");
        if (!deleteResult.IsSuccess) return deleteResult;

        var indexResult = await _storage.LoadAsync<List<string>>(IndexKey);
        if (indexResult.IsSuccess && indexResult.Value != null && indexResult.Value.Remove(folderId))
            await _storage.SaveAsync(IndexKey, indexResult.Value);

        return Result.Success();
    }
}
