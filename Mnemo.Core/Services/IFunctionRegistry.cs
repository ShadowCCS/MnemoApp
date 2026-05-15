using System.Collections.Generic;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface IFunctionRegistry
{
    void RegisterTool(AIToolDefinition tool);
    void ClearTools();
    IEnumerable<AIToolDefinition> GetTools();
}

