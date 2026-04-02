using System.Text.Json;
using Mnemo.Core.Models.Tools;
using Mnemo.Core.Services;

namespace Mnemo.Infrastructure.Services.Tools;

public sealed class ToolResultFormatter : IToolResultFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Format(ToolInvocationResult result)
    {
        var payload = new
        {
            ok = result.Ok,
            code = result.Code,
            message = result.Message,
            data = result.Data
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}
