using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mnemo.UI.Components.BlockEditor;

public class CommandItem
{
    public string Icon { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public BlockType BlockType { get; set; }
}

public partial class SlashCommandMenu : UserControl
{
    private readonly List<CommandItem> _allCommandItems;
    private List<CommandItem> _filteredCommandItems;
    private string _filterText = string.Empty;

    public event Action<BlockType>? CommandSelected;

    public SlashCommandMenu()
    {
        _allCommandItems = InitializeCommandItems();
        _filteredCommandItems = new List<CommandItem>(_allCommandItems);
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        EnsureItemsSourceSet();
        Loaded -= OnLoaded;
    }

    private void EnsureItemsSourceSet()
    {
        var itemsControl = this.FindControl<ItemsControl>("CommandItems");
        if (itemsControl != null)
        {
            if (itemsControl.ItemsSource != _filteredCommandItems)
            {
                itemsControl.ItemsSource = _filteredCommandItems;
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private List<CommandItem> InitializeCommandItems()
    {
        var loc = (Application.Current as App)?.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
        string T(string key, string ns = "NotesEditor") => loc?.T(key, ns) ?? key;

        return new List<CommandItem>
        {
            new() { Icon = "T", Name = T("Text"), Description = T("TextDescription"), BlockType = BlockType.Text },
            new() { Icon = "H1", Name = T("Heading1"), Description = T("Heading1Description"), BlockType = BlockType.Heading1 },
            new() { Icon = "H2", Name = T("Heading2"), Description = T("Heading2Description"), BlockType = BlockType.Heading2 },
            new() { Icon = "H3", Name = T("Heading3"), Description = T("Heading3Description"), BlockType = BlockType.Heading3 },
            new() { Icon = "•", Name = T("BulletList"), Description = T("BulletListDescription"), BlockType = BlockType.BulletList },
            new() { Icon = "1.", Name = T("NumberedList"), Description = T("NumberedListDescription"), BlockType = BlockType.NumberedList },
            new() { Icon = "☑", Name = T("Checklist"), Description = T("ChecklistDescription"), BlockType = BlockType.Checklist },
            new() { Icon = "Q", Name = T("Quote"), Description = T("QuoteDescription"), BlockType = BlockType.Quote },
            new() { Icon = "</>", Name = T("Code"), Description = T("CodeDescription"), BlockType = BlockType.Code },
            new() { Icon = "—", Name = T("Divider"), Description = T("DividerDescription"), BlockType = BlockType.Divider }
        };
    }

    public void UpdateFilter(string filterText)
    {
        if (_filterText == filterText) return;
        
        _filterText = filterText;
        FilterItems(filterText);
        EnsureItemsSourceSet();
    }

    private void FilterItems(string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText) || filterText == "/")
        {
            _filteredCommandItems = new List<CommandItem>(_allCommandItems);
        }
        else
        {
            var searchTerm = filterText.TrimStart('/').ToLowerInvariant();
            _filteredCommandItems = _allCommandItems
                .Where(item => item.Name.ToLowerInvariant().Contains(searchTerm) ||
                              item.Description.ToLowerInvariant().Contains(searchTerm))
                .ToList();
        }
    }

    public void HandleEnter()
    {
        if (_filteredCommandItems.Count == 0) return;
        CommandSelected?.Invoke(_filteredCommandItems[0].BlockType);
    }

    public void HandleItemClick(CommandItem command)
    {
        if (command == null) return;
        CommandSelected?.Invoke(command.BlockType);
    }

    private void CommandItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not CommandItem command)
        {
            return;
        }

        HandleItemClick(command);
        e.Handled = true;
    }
}


