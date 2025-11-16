using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using MnemoApp.Modules.Notes.Models;
using MnemoApp.UI.Components.BlockEditor.BlockComponents.Text;
using MnemoApp.UI.Components.BlockEditor.BlockComponents.Heading1;
using MnemoApp.UI.Components.BlockEditor.BlockComponents.Heading2;
using MnemoApp.UI.Components.BlockEditor.BlockComponents.Heading3;
using MnemoApp.UI.Components.BlockEditor.BlockComponents.Quote;
using MnemoApp.UI.Components.BlockEditor.BlockComponents.Code;
using MnemoApp.UI.Components.BlockEditor.BlockComponents.Divider;
using MnemoApp.UI.Components.BlockEditor.BlockComponents.BulletList;
using MnemoApp.UI.Components.BlockEditor.BlockComponents.NumberedList;
using MnemoApp.UI.Components.BlockEditor.BlockComponents.Checklist;
using System;
using System.Globalization;

namespace MnemoApp.UI.Components.BlockEditor.BlockComponents;

public class BlockTypeToComponentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not BlockType blockType)
            return null;

        return blockType switch
        {
            BlockType.Text => new TextBlockComponent(),
            BlockType.Heading1 => new Heading1BlockComponent(),
            BlockType.Heading2 => new Heading2BlockComponent(),
            BlockType.Heading3 => new Heading3BlockComponent(),
            BlockType.Quote => new QuoteBlockComponent(),
            BlockType.Code => new CodeBlockComponent(),
            BlockType.Divider => new DividerBlockComponent(),
            BlockType.BulletList => new BulletListBlockComponent(),
            BlockType.NumberedList => new NumberedListBlockComponent(),
            BlockType.Checklist => new ChecklistBlockComponent(),
            _ => new TextBlockComponent()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

