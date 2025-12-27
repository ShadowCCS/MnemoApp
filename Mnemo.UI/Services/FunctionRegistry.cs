using System.Collections.Generic;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.UI.Services;

public class FunctionRegistry : IFunctionRegistry
{
    private readonly List<AIToolDefinition> _tools = new();

    public void RegisterTool(AIToolDefinition tool)
    {
        _tools.Add(tool);
    }

    public IEnumerable<AIToolDefinition> GetTools() => _tools;
}


