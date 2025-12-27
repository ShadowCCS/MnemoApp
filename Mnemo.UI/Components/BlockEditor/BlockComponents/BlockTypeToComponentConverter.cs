using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Mnemo.Core.Models;
using Mnemo.UI.Components.BlockEditor.BlockComponents.Text;
using Mnemo.UI.Components.BlockEditor.BlockComponents.Heading1;
using Mnemo.UI.Components.BlockEditor.BlockComponents.Heading2;
using Mnemo.UI.Components.BlockEditor.BlockComponents.Heading3;
using Mnemo.UI.Components.BlockEditor.BlockComponents.Quote;
using Mnemo.UI.Components.BlockEditor.BlockComponents.Code;
using Mnemo.UI.Components.BlockEditor.BlockComponents.Divider;
using Mnemo.UI.Components.BlockEditor.BlockComponents.BulletList;
using Mnemo.UI.Components.BlockEditor.BlockComponents.NumberedList;
using Mnemo.UI.Components.BlockEditor.BlockComponents.Checklist;
using System;
using System.Globalization;

namespace Mnemo.UI.Components.BlockEditor.BlockComponents;

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


