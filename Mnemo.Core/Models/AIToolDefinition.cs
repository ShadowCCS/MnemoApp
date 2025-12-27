using System;
using System.Threading.Tasks;

namespace Mnemo.Core.Models;

public record AIToolDefinition(
    string Name,
    string Description,
    Type ParametersType,
    Func<object, Task<string>> Handler);

