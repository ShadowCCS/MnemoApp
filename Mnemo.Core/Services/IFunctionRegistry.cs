using System.Collections.Generic;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface IFunctionRegistry
{
    void RegisterTool(AIToolDefinition tool);
    IEnumerable<AIToolDefinition> GetTools();
}

