using System;
using System.Threading.Tasks;
using Mnemo.Core.Models.Tools;

namespace Mnemo.Core.Models;

public record AIToolDefinition(
    string Name,
    string Description,
    Type ParametersType,
    Func<object, Task<ToolInvocationResult>> Handler);

