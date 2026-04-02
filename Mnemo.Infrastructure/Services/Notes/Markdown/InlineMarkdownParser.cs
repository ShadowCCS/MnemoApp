using System.Collections.Generic;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Mnemo.Core.Formatting;
using Mnemo.Core.Models;

namespace Mnemo.Infrastructure.Services.Notes.Markdown;

/// <summary>Parses inline markdown into <see cref="InlineRun"/> lists using Markdig (shared with block editor).</summary>
public static class InlineMarkdownParser
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static List<InlineRun> ToRuns(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return new List<InlineRun> { InlineRun.Plain(string.Empty) };

        var doc = global::Markdig.Markdown.Parse(markdown, Pipeline);
        var runs = new List<InlineRun>();
        var firstBlock = true;
        foreach (var block in doc)
        {
            if (!firstBlock)
                runs.Add(InlineRun.Plain("\n"));
            firstBlock = false;

            switch (block)
            {
                case ParagraphBlock paragraph:
                    VisitInlines(paragraph.Inline, InlineStyle.Default, runs);
                    break;
                case HeadingBlock heading:
                    VisitInlines(heading.Inline, InlineStyle.Default, runs);
                    break;
                case ThematicBreakBlock:
                    break;
                case FencedCodeBlock fenced:
                    runs.Add(new InlineRun(LinesToString(fenced.Lines), InlineStyle.Default));
                    break;
                case CodeBlock cb:
                    runs.Add(new InlineRun(LinesToString(cb.Lines), InlineStyle.Default));
                    break;
                case QuoteBlock quote:
                    AppendBlockContainer(quote, runs);
                    break;
                case ListBlock list:
                    AppendBlockContainer(list, runs);
                    break;
                default:
                    if (block is LeafBlock { Inline: { } leafInline })
                        VisitInlines(leafInline, InlineStyle.Default, runs);
                    break;
            }
        }

        var normalized = InlineRunFormatApplier.Normalize(runs);
        if (normalized.Count == 0)
            normalized.Add(InlineRun.Plain(string.Empty));
        return normalized;
    }

    private static void AppendBlockContainer(ContainerBlock container, List<InlineRun> runs)
    {
        var first = true;
        foreach (var child in container)
        {
            if (!first)
                runs.Add(InlineRun.Plain("\n"));
            first = false;
            if (child is ParagraphBlock p)
                VisitInlines(p.Inline, InlineStyle.Default, runs);
            else if (child is QuoteBlock q)
                AppendBlockContainer(q, runs);
            else if (child is ListBlock l)
                AppendBlockContainer(l, runs);
            else if (child is ListItemBlock item)
                AppendBlockContainer(item, runs);
            else if (child is LeafBlock { Inline: { } inline })
                VisitInlines(inline, InlineStyle.Default, runs);
        }
    }

    private static void VisitInlines(Inline? inline, InlineStyle style, List<InlineRun> runs)
    {
        if (inline == null) return;

        switch (inline)
        {
            case LiteralInline literal:
                if (literal.Content.Length > 0)
                    runs.Add(new InlineRun(literal.Content.ToString(), style));
                break;

            case CodeInline code:
                runs.Add(new InlineRun(code.Content, style.WithSet(InlineFormatKind.Code)));
                break;

            case EmphasisInline emphasis:
                VisitEmphasis(emphasis, style, runs);
                break;

            case LineBreakInline:
                runs.Add(new InlineRun("\n", style));
                break;

            case LinkInline link:
                foreach (var c in link)
                    VisitInlines(c, style, runs);
                break;

            case AutolinkInline auto:
                runs.Add(new InlineRun(auto.Url ?? string.Empty, style));
                break;

            case HtmlInline:
                break;

            case ContainerInline container:
                foreach (var c in container)
                    VisitInlines(c, style, runs);
                break;
        }
    }

    private static string LinesToString(Markdig.Helpers.StringLineGroup lines)
    {
        var sb = new StringBuilder();
        var i = 0;
        foreach (var line in lines)
        {
            if (i++ > 0) sb.AppendLine();
            sb.Append(line.ToString());
        }
        return sb.ToString();
    }

    private static void VisitEmphasis(EmphasisInline emphasis, InlineStyle style, List<InlineRun> runs)
    {
        var next = emphasis.DelimiterChar switch
        {
            '~' when emphasis.DelimiterCount >= 2 => style with { Strikethrough = true },
            '*' or '_' => emphasis.DelimiterCount switch
            {
                >= 3 => style with { Bold = true, Italic = true },
                2 => style with { Bold = true },
                _ => style with { Italic = true }
            },
            _ => style
        };

        foreach (var c in emphasis)
            VisitInlines(c, next, runs);
    }
}
