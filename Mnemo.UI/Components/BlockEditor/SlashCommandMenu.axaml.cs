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
using System.Globalization;
using System.Linq;
using System.Text;

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
    private static readonly Dictionary<string, string> DigitToWord = new(StringComparer.Ordinal)
    {
        ["0"] = "zero",
        ["1"] = "one",
        ["2"] = "two",
        ["3"] = "three",
        ["4"] = "four",
        ["5"] = "five",
        ["6"] = "six",
        ["7"] = "seven",
        ["8"] = "eight",
        ["9"] = "nine"
    };

    private static readonly Dictionary<string, string> WordToDigit = DigitToWord
        .ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.Ordinal);

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
            new() { IconPath = iconBase + "h-3.svg", Name = T("Heading4"), Description = T("Heading4Description"), Shortcut = "####", BlockType = BlockType.Heading4 },
            new() { IconPath = iconBase + "list.svg", Name = T("BulletList"), Description = T("BulletListDescription"), Shortcut = "-", BlockType = BlockType.BulletList },
            new() { IconPath = iconBase + "list-numbers.svg", Name = T("NumberedList"), Description = T("NumberedListDescription"), Shortcut = "1.", BlockType = BlockType.NumberedList },
            new() { IconPath = iconBase + "list-check.svg", Name = T("Checklist"), Description = T("ChecklistDescription"), Shortcut = "[]", BlockType = BlockType.Checklist },
            new() { IconPath = iconBase + "quote.svg", Name = T("Quote"), Description = T("QuoteDescription"), Shortcut = "\"", BlockType = BlockType.Quote },
            new() { IconPath = iconBase + "math-function.svg", Name = T("Code"), Description = T("CodeDescription"), Shortcut = null, BlockType = BlockType.Code },
            new() { IconPath = iconBase + "separator.svg", Name = T("Divider"), Description = T("DividerDescription"), Shortcut = "---", BlockType = BlockType.Divider },
            new() { IconPath = "avares://Mnemo.UI/Icons/Editor/two-column.svg", Name = T("TwoColumn"), Description = T("TwoColumnDescription"), Shortcut = null, BlockType = BlockType.TwoColumn },
            new() { IconPath = iconBase + "photo.svg", Name = T("Image"), Description = T("ImageDescription"), Shortcut = null, BlockType = BlockType.Image },
            new() { IconPath = iconBase + "math-equation.svg", Name = T("Equation"), Description = T("EquationDescription"), Shortcut = null, BlockType = BlockType.Equation }
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
            var searchTerm = NormalizeSearchText(filterText.TrimStart('/'));
            if (string.IsNullOrEmpty(searchTerm))
            {
                _filteredCommandItems = new List<CommandItem>(_allCommandItems);
                return;
            }

            _filteredCommandItems = _allCommandItems
                .Where(item => IsMatch(item, searchTerm))
                .ToList();
        }
    }

    private static bool IsMatch(CommandItem item, string normalizedSearchTerm)
    {
        if (string.IsNullOrEmpty(normalizedSearchTerm))
            return true;

        foreach (var candidate in GetSearchCandidates(item))
        {
            if (candidate.Contains(normalizedSearchTerm, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> GetSearchCandidates(CommandItem item)
    {
        var rawCandidates = new[]
        {
            item.Name,
            item.Description,
            item.Shortcut ?? string.Empty,
            item.BlockType.ToString()
        };

        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in rawCandidates)
        {
            var normalized = NormalizeSearchText(raw);
            if (!string.IsNullOrEmpty(normalized))
                unique.Add(normalized);

            foreach (var expanded in ExpandNumericAliases(normalized))
                unique.Add(expanded);
        }

        return unique;
    }

    private static IEnumerable<string> ExpandNumericAliases(string normalizedText)
    {
        if (string.IsNullOrEmpty(normalizedText))
            yield break;

        var parts = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            yield break;

        var hasAnyReplacement = false;
        var digitToWordParts = new string[parts.Length];
        var wordToDigitParts = new string[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            var token = parts[i];
            if (DigitToWord.TryGetValue(token, out var word))
            {
                digitToWordParts[i] = word;
                wordToDigitParts[i] = token;
                hasAnyReplacement = true;
            }
            else if (WordToDigit.TryGetValue(token, out var digit))
            {
                digitToWordParts[i] = token;
                wordToDigitParts[i] = digit;
                hasAnyReplacement = true;
            }
            else
            {
                digitToWordParts[i] = token;
                wordToDigitParts[i] = token;
            }
        }

        if (!hasAnyReplacement)
            yield break;

        yield return string.Join(' ', digitToWordParts);
        yield return string.Join(' ', wordToDigitParts);
    }

    private static string NormalizeSearchText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalizedForm = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalizedForm.Length);
        var previousWasSpace = false;

        foreach (var ch in normalizedForm)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder
            .ToString()
            .Trim();
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


