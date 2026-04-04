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
    /// <summary>Full avares path to the SVG icon (e.g. avares://Mnemo.UI/Icons/SlashCommand/letter-t.svg).</summary>
    public string IconPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>Optional shortcut hint shown on the right (e.g. "#", "1.", "-").</summary>
    public string? Shortcut { get; set; }
    public BlockType BlockType { get; set; }
}

public partial class SlashCommandMenu : UserControl
{
    private readonly List<CommandItem> _allCommandItems;
    private List<CommandItem> _filteredCommandItems;
    private string _filterText = string.Empty;
    private int _selectedIndex;
    private ListBox? _commandListBox;

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
        _commandListBox = this.FindControl<ListBox>("CommandListBox");
        var closeMenuText = this.FindControl<TextBlock>("CloseMenuTextBlock");
        if (closeMenuText != null)
        {
            var loc = (Application.Current as App)?.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
            string T(string key, string ns = "NotesEditor") => loc?.T(key, ns) ?? key;
            closeMenuText.Text = T("CloseMenu");
        }
        EnsureItemsSourceSet();
        SyncSelectedIndex();
        Loaded -= OnLoaded;
    }

    private void EnsureItemsSourceSet()
    {
        if (_commandListBox == null) return;
        if (_commandListBox.ItemsSource != _filteredCommandItems)
        {
            _commandListBox.ItemsSource = _filteredCommandItems;
        }
    }

    private void SyncSelectedIndex()
    {
        if (_commandListBox == null) return;
        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _filteredCommandItems.Count - 1));
        if (_commandListBox.SelectedIndex != _selectedIndex)
        {
            _commandListBox.SelectedIndex = _selectedIndex;
        }
        if (_selectedIndex >= 0 && _commandListBox.ContainerFromIndex(_selectedIndex) is Control container)
        {
            container.BringIntoView();
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

        const string iconBase = "avares://Mnemo.UI/Icons/SlashCommand/";
        return new List<CommandItem>
        {
            new() { IconPath = iconBase + "letter-t.svg", Name = T("Text"), Description = T("TextDescription"), Shortcut = null, BlockType = BlockType.Text },
            new() { IconPath = iconBase + "h-1.svg", Name = T("Heading1"), Description = T("Heading1Description"), Shortcut = "#", BlockType = BlockType.Heading1 },
            new() { IconPath = iconBase + "h-2.svg", Name = T("Heading2"), Description = T("Heading2Description"), Shortcut = "##", BlockType = BlockType.Heading2 },
            new() { IconPath = iconBase + "h-3.svg", Name = T("Heading3"), Description = T("Heading3Description"), Shortcut = "###", BlockType = BlockType.Heading3 },
            new() { IconPath = iconBase + "list.svg", Name = T("BulletList"), Description = T("BulletListDescription"), Shortcut = "-", BlockType = BlockType.BulletList },
            new() { IconPath = iconBase + "list-numbers.svg", Name = T("NumberedList"), Description = T("NumberedListDescription"), Shortcut = "1.", BlockType = BlockType.NumberedList },
            new() { IconPath = iconBase + "list-check.svg", Name = T("Checklist"), Description = T("ChecklistDescription"), Shortcut = "[]", BlockType = BlockType.Checklist },
            new() { IconPath = iconBase + "quote.svg", Name = T("Quote"), Description = T("QuoteDescription"), Shortcut = "\"", BlockType = BlockType.Quote },
            new() { IconPath = iconBase + "math-function.svg", Name = T("Code"), Description = T("CodeDescription"), Shortcut = null, BlockType = BlockType.Code },
            new() { IconPath = iconBase + "separator.svg", Name = T("Divider"), Description = T("DividerDescription"), Shortcut = "---", BlockType = BlockType.Divider },
            new() { IconPath = iconBase + "photo.svg", Name = T("Image"), Description = T("ImageDescription"), Shortcut = null, BlockType = BlockType.Image }
        };
    }

    public void UpdateFilter(string filterText)
    {
        if (_filterText == filterText) return;
        
        _filterText = filterText;
        FilterItems(filterText);
        _selectedIndex = 0;
        EnsureItemsSourceSet();
        SyncSelectedIndex();
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
        _selectedIndex = Math.Clamp(_selectedIndex, 0, _filteredCommandItems.Count - 1);
        CommandSelected?.Invoke(_filteredCommandItems[_selectedIndex].BlockType);
    }

    public void HandleUp()
    {
        if (_filteredCommandItems.Count == 0) return;
        _selectedIndex = Math.Max(0, _selectedIndex - 1);
        SyncSelectedIndex();
    }

    public void HandleDown()
    {
        if (_filteredCommandItems.Count == 0) return;
        _selectedIndex = Math.Min(_filteredCommandItems.Count - 1, _selectedIndex + 1);
        SyncSelectedIndex();
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


