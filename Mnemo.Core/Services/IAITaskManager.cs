using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface IAITaskManager
{
    Task<string> QueueTaskAsync(IAITask task);
    IAITask? GetTask(string taskId);
    IEnumerable<IAITask> GetAllTasks();
    event Action<IAITask>? TaskUpdated;
}



