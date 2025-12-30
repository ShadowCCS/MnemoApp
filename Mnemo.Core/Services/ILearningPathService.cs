using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface ILearningPathService
{
    Task<IEnumerable<LearningPath>> GetAllPathsAsync();
    Task<LearningPath?> GetPathAsync(string id);
    Task<Result> SavePathAsync(LearningPath path);
    Task<Result> DeletePathAsync(string id);

    event Action<LearningPath>? PathUpdated;
}



