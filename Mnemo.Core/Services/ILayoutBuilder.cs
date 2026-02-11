namespace Mnemo.Core.Services;

/// <summary>
/// Builds layout boxes from parsed LaTeX AST nodes.
/// </summary>
public interface ILayoutBuilder
{
    /// <summary>
    /// Builds a layout box tree from a LaTeX AST node.
    /// </summary>
    /// <param name="node">The parsed LaTeX node.</param>
    /// <returns>The root layout box.</returns>
    object BuildLayout(object node);
}
