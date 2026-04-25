using System;
using System.Collections.Generic;
using System.Linq;
using Mnemo.Core.Formatting;
using Mnemo.Core.Models;
using Mnemo.Core.Models.Clipboard;

using Mnemo.Infrastructure.Services.Notes.Markdown;

namespace Mnemo.UI.Components.BlockEditor;

public static class NoteClipboardMapper
{
    public static NoteClipboardDocument ToDocument(IEnumerable<BlockViewModel> blocks)
    {
        var list = new List<NoteClipboardBlockDto>();
        foreach (var vm in blocks)
            list.Add(ToDto(vm));

        return new NoteClipboardDocument { SchemaVersion = 1, Blocks = list };
    }

    public static List<BlockViewModel> ToViewModels(NoteClipboardDocument document, int firstOrder = 0)
    {
        var list = new List<BlockViewModel>();
        int order = firstOrder;
        foreach (var dto in document.Blocks)
        {
            if (dto.Type == BlockType.TwoColumn && dto.Children is { Count: >= 2 })
            {
                list.Add(BuildViewModelFromDto(dto, order));
                order++;
                continue;
            }

            list.Add(ToViewModel(dto, order++));
        }

        return list;
    }

    private static NoteClipboardBlockDto ToDto(BlockViewModel vm)
    {
        if (vm is TwoColumnBlockViewModel tc)
        {
            var ratio = tc.ColumnSplitRatio;
            return new NoteClipboardBlockDto
            {
                Type = BlockType.TwoColumn,
                Content = string.Empty,
                Runs = new List<NoteClipboardRunDto>(),
                ColumnSplitRatio = ratio,
                Children =
                [
                    new NoteClipboardBlockDto
                    {
                        Type = BlockType.ColumnGroup,
                        Children = tc.LeftColumnBlocks.Select(ToDto).ToList()
                    },
                    new NoteClipboardBlockDto
                    {
                        Type = BlockType.ColumnGroup,
                        Children = tc.RightColumnBlocks.Select(ToDto).ToList()
                    }
                ]
            };
        }

        var dto = new NoteClipboardBlockDto
        {
            Type = vm.Type,
            Runs = vm.Spans.Select(ToRunDto).ToList(),
            ListNumberIndex = vm.Type == BlockType.NumberedList ? vm.ListNumberIndex : null
        };
        if (vm.Type == BlockType.Checklist)
            dto.IsChecked = vm.IsChecked;
        if (vm.Type == BlockType.Code)
            dto.CodeLanguage = vm.CodeLanguage;

        if (vm.Type == BlockType.Equation)
            dto.EquationLatex = vm.EquationLatex;

        if (vm.Type == BlockType.Image)
        {
            dto.ImagePath = vm.ImagePath;
            dto.ImageAlt = vm.Content;
            if (vm.ImageWidth > 0)
                dto.ImageWidth = vm.ImageWidth;
            if (!string.IsNullOrEmpty(vm.ImageAlign) && vm.ImageAlign != "left")
                dto.ImageAlign = vm.ImageAlign;
        }

        if (vm.Meta.TryGetValue(ColumnPairHelper.PairIdKey, out var pairId) && pairId != null)
            dto.ColumnPairId = pairId.ToString();
        if (vm.Meta.TryGetValue(ColumnPairHelper.SideKey, out var colSide) && colSide != null)
            dto.ColumnSide = colSide.ToString();
        if (ColumnPairHelper.GetColumnSide(vm) == ColumnPairHelper.Left)
            dto.ColumnSplitRatio = vm.ColumnSplitRatio;

        dto.Content = vm.Content;
        return dto;
    }

    private static NoteClipboardRunDto ToRunDto(InlineSpan span) => span switch
    {
        EquationSpan eq => new NoteClipboardRunDto
        {
            Text = string.Empty,
            Bold = eq.Style.Bold,
            Italic = eq.Style.Italic,
            Underline = eq.Style.Underline,
            Strikethrough = eq.Style.Strikethrough,
            Code = eq.Style.Code,
            Highlight = eq.Style.Highlight,
            BackgroundColor = eq.Style.BackgroundColor,
            Subscript = eq.Style.Subscript,
            Superscript = eq.Style.Superscript,
            EquationLatex = eq.Latex
        },
        FractionSpan f => new NoteClipboardRunDto
        {
            Text = string.Empty,
            Bold = f.Style.Bold,
            Italic = f.Style.Italic,
            Underline = f.Style.Underline,
            Strikethrough = f.Style.Strikethrough,
            Code = f.Style.Code,
            Highlight = f.Style.Highlight,
            BackgroundColor = f.Style.BackgroundColor,
            Subscript = f.Style.Subscript,
            Superscript = f.Style.Superscript,
            FractionNumerator = f.Numerator,
            FractionDenominator = f.Denominator
        },
        TextSpan t => new NoteClipboardRunDto
        {
            Text = t.Text,
            Bold = t.Style.Bold,
            Italic = t.Style.Italic,
            Underline = t.Style.Underline,
            Strikethrough = t.Style.Strikethrough,
            Code = t.Style.Code,
            Highlight = t.Style.Highlight,
            BackgroundColor = t.Style.BackgroundColor,
            Subscript = t.Style.Subscript,
            Superscript = t.Style.Superscript,
            EquationLatex = null
        },
        _ => new NoteClipboardRunDto()
    };

    private static BlockViewModel ToViewModel(NoteClipboardBlockDto dto, int order) =>
        BuildViewModelFromDto(dto, order);

    private static BlockViewModel BuildViewModelFromDto(NoteClipboardBlockDto dto, int order)
    {
        if (dto.Type == BlockType.TwoColumn && dto.Children is { Count: >= 2 })
        {
            var tc = new TwoColumnBlockViewModel(order);
            if (dto.ColumnSplitRatio is > 0 and < 1)
                tc.ColumnSplitRatio = dto.ColumnSplitRatio.Value;

            var o = order;
            void IngestSide(NoteClipboardBlockDto side, bool left)
            {
                if (side.Type == BlockType.ColumnGroup && side.Children is { Count: > 0 })
                {
                    foreach (var ch in side.Children!)
                    {
                        var childVm = BuildViewModelFromDto(ch, o++);
                        BlockHierarchy.WireChildOwnership(tc, childVm, left);
                        (left ? tc.LeftColumnBlocks : tc.RightColumnBlocks).Add(childVm);
                    }
                }
                else
                {
                    var childVm = BuildViewModelFromDto(side, o++);
                    BlockHierarchy.WireChildOwnership(tc, childVm, left);
                    (left ? tc.LeftColumnBlocks : tc.RightColumnBlocks).Add(childVm);
                }
            }

            IngestSide(dto.Children[0], true);
            IngestSide(dto.Children[1], false);
            if (tc.LeftColumnBlocks.Count == 0)
            {
                var ph = BlockFactory.CreateBlock(BlockType.Text, o++);
                BlockHierarchy.WireChildOwnership(tc, ph, true);
                tc.LeftColumnBlocks.Add(ph);
            }

            if (tc.RightColumnBlocks.Count == 0)
            {
                var ph = BlockFactory.CreateBlock(BlockType.Text, o++);
                BlockHierarchy.WireChildOwnership(tc, ph, false);
                tc.RightColumnBlocks.Add(ph);
            }

            return tc;
        }

        var vm = BlockFactory.CreateBlock(dto.Type, order);
        if (!string.IsNullOrEmpty(dto.CodeLanguage))
            vm.CodeLanguage = dto.CodeLanguage;

        if (dto.Type == BlockType.Equation && !string.IsNullOrEmpty(dto.EquationLatex))
            vm.EquationLatex = dto.EquationLatex;

        if (dto.Type == BlockType.Checklist && dto.IsChecked.HasValue)
            vm.IsChecked = dto.IsChecked.Value;

        if (dto.Type == BlockType.NumberedList && dto.ListNumberIndex is { } n)
            vm.ListNumberIndex = n;

        if (dto.Type == BlockType.Image)
        {
            vm.ImagePath = dto.ImagePath ?? string.Empty;
            vm.ImageWidth = dto.ImageWidth is > 0 ? dto.ImageWidth.Value : 0;
            vm.ImageAlign = string.IsNullOrEmpty(dto.ImageAlign) ? "left" : dto.ImageAlign;
            var alt = dto.ImageAlt ?? string.Empty;
            vm.SetSpans(new List<InlineSpan> { InlineSpan.Plain(alt) });
            ApplyColumnClipboardMeta(vm, dto);
            return vm;
        }

        if (dto.Runs is { Count: > 0 })
        {
            var spans = dto.Runs.Select(FromRunDto).ToList();
            vm.SetSpans(InlineSpanFormatApplier.Normalize(spans));
        }
        else if (!string.IsNullOrEmpty(dto.Content))
            vm.SetSpans(InlineMarkdownParser.ToSpans(dto.Content));
        else
            vm.SetSpans(new List<InlineSpan> { InlineSpan.Plain(string.Empty) });

        ApplyColumnClipboardMeta(vm, dto);
        return vm;
    }

    private static void ApplyColumnClipboardMeta(BlockViewModel vm, NoteClipboardBlockDto dto)
    {
        if (!string.IsNullOrEmpty(dto.ColumnPairId))
            vm.Meta[ColumnPairHelper.PairIdKey] = dto.ColumnPairId;
        if (!string.IsNullOrEmpty(dto.ColumnSide))
            vm.Meta[ColumnPairHelper.SideKey] = dto.ColumnSide;
        if (dto.ColumnSplitRatio is > 0 and < 1 && ColumnPairHelper.GetColumnSide(vm) == ColumnPairHelper.Left)
            vm.Meta["columnSplitRatio"] = dto.ColumnSplitRatio.Value;
    }

    private static InlineSpan FromRunDto(NoteClipboardRunDto dto)
    {
        var style = new TextStyle(
            Bold: dto.Bold,
            Italic: dto.Italic,
            Underline: dto.Underline,
            Strikethrough: dto.Strikethrough,
            Code: dto.Code,
            Highlight: dto.Highlight,
            BackgroundColor: dto.BackgroundColor,
            Subscript: dto.Subscript,
            Superscript: dto.Superscript);
        if (!string.IsNullOrEmpty(dto.EquationLatex))
            return new EquationSpan(dto.EquationLatex, style);
        if (dto.FractionNumerator.HasValue && dto.FractionDenominator.HasValue)
            return new FractionSpan(dto.FractionNumerator.Value, Math.Max(1, dto.FractionDenominator.Value), style);
        return new TextSpan(dto.Text ?? string.Empty, style);
    }
}
