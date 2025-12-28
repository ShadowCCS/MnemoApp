using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Mnemo.Core.Models.Markdown;

namespace Mnemo.UI.Services;

using Avalonia.Media;
// ...
public interface IMarkdownRenderer
{
    Task<Control> RenderAsync(string markdown, Dictionary<string, MarkdownSpecialInline> specialInlines, IBrush? foreground = null);
}

