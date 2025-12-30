using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services;

public class LearningPathService : ILearningPathService
{
    private readonly IStorageProvider _storage;
    private const string IndexKey = "learning_paths_index";

    public LearningPathService(IStorageProvider storage)
    {
        _storage = storage;
    }

    public async Task<IEnumerable<LearningPath>> GetAllPathsAsync()
    {
        var indexResult = await _storage.LoadAsync<List<string>>(IndexKey);
        if (!indexResult.IsSuccess || indexResult.Value == null)
            return Enumerable.Empty<LearningPath>();

        var paths = new List<LearningPath>();
        foreach (var id in indexResult.Value)
        {
            var pathResult = await _storage.LoadAsync<LearningPath>($"path_{id}");
            if (pathResult.IsSuccess && pathResult.Value != null)
            {
                paths.Add(pathResult.Value);
            }
        }
        return paths;
    }

    public async Task<LearningPath?> GetPathAsync(string id)
    {
        var result = await _storage.LoadAsync<LearningPath>($"path_{id}");
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<Result> SavePathAsync(LearningPath path)
    {
        var saveResult = await _storage.SaveAsync($"path_{path.PathId}", path);
        if (!saveResult.IsSuccess) return saveResult;

        var indexResult = await _storage.LoadAsync<List<string>>(IndexKey);
        var index = indexResult.Value ?? new List<string>();

        if (!index.Contains(path.PathId))
        {
            index.Add(path.PathId);
            return await _storage.SaveAsync(IndexKey, index);
        }

        return Result.Success();
    }

    public async Task<Result> DeletePathAsync(string id)
    {
        var deleteResult = await _storage.DeleteAsync($"path_{id}");
        if (!deleteResult.IsSuccess) return deleteResult;

        var indexResult = await _storage.LoadAsync<List<string>>(IndexKey);
        if (indexResult.IsSuccess && indexResult.Value != null)
        {
            if (indexResult.Value.Remove(id))
            {
                return await _storage.SaveAsync(IndexKey, indexResult.Value);
            }
        }

        return Result.Success();
    }
}


