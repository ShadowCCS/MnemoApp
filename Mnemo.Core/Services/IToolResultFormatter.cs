using Mnemo.Core.Models.Tools;

namespace Mnemo.Core.Services;

/// <summary>Formats <see cref="ToolInvocationResult"/> into text for tool-result messages.</summary>
public interface IToolResultFormatter
{
    string Format(ToolInvocationResult result);
}
