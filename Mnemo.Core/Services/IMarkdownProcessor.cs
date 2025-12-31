using System.Collections.Generic;
using Mnemo.Core.Models.Markdown;

namespace Mnemo.Core.Services;

public interface IMarkdownProcessor
{
    (string ProcessedSource, Dictionary<string, MarkdownSpecialInline> SpecialInlines) ExtractSpecialInlines(string source);
}