using System.Collections.Generic;
using System.Threading.Tasks;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface IAIModelRegistry
{
    Task<IEnumerable<AIModelManifest>> GetAvailableModelsAsync();
    Task<AIModelManifest?> GetModelAsync(string modelId);
    Task RefreshAsync();
}





