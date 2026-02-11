using System.Threading;
using System.Threading.Tasks;

namespace Mnemo.Core.Services;

/// <summary>
/// Service for parsing and building LaTeX layout trees.
/// </summary>
public interface ILaTeXEngine
{
    /// <summary>
    /// Parses LaTeX markup and builds a layout box tree for rendering.
    /// </summary>
    /// <param name="tex">The LaTeX markup string.</param>
    /// <param name="fontSize">The base font size in pixels.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The root layout box, or null if parsing failed.</returns>
    Task<object?> BuildLayoutAsync(string tex, double fontSize = 16.0, CancellationToken cancellationToken = default);
}