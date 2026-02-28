using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using Mnemo.Core.Models;
using Mnemo.Core.Services;

namespace Mnemo.UI.Services;

public class SidebarService : ISidebarService, INotifyPropertyChanged
{
    private bool _isCollapsed;
    private readonly ILocalizationService _localizationService;

    private static readonly Dictionary<string, int> DefaultCategoryOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        { "MainHub", 0 },
        { "Library", 1 },
        { "Ecosystem", 2 }
    };

    private readonly List<(SidebarCategory category, string nameKey, string ns)> _categoryKeys = new();
    private readonly List<(SidebarItem item, string labelKey, string ns)> _itemKeys = new();

    public ObservableCollection<SidebarCategory> Categories { get; } = new();

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (_isCollapsed != value)
            {
                _isCollapsed = value;
                OnPropertyChanged();
            }
        }
    }

    public SidebarService(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
        _localizationService.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var (category, nameKey, ns) in _categoryKeys)
            category.Name = _localizationService.T(nameKey, ns);
        foreach (var (item, labelKey, ns) in _itemKeys)
            item.Label = _localizationService.T(labelKey, ns);
    }

    public void RegisterItem(string labelKey, string route, string icon, string categoryKey = "General", int? categoryOrder = null, int itemOrder = int.MaxValue, string ns = "Sidebar")
    {
        var category = _categoryKeys.FirstOrDefault(t => string.Equals(t.nameKey, categoryKey, StringComparison.OrdinalIgnoreCase) && t.ns == ns).category;
        if (category == null)
        {
            var order = categoryOrder ??
                       (DefaultCategoryOrder.TryGetValue(categoryKey, out var defaultOrder) ? defaultOrder : int.MaxValue);
            var resolvedCategoryName = _localizationService.T(categoryKey, ns);
            category = new SidebarCategory(resolvedCategoryName, order);
            _categoryKeys.Add((category, categoryKey, ns));

            var insertIndex = Categories.Count;
            for (int i = 0; i < Categories.Count; i++)
            {
                if (Categories[i].Order > order)
                {
                    insertIndex = i;
                    break;
                }
            }
            Categories.Insert(insertIndex, category);
        }

        var resolvedLabel = _localizationService.T(labelKey, ns);
        var item = new SidebarItem(resolvedLabel, route, icon, itemOrder);
        _itemKeys.Add((item, labelKey, ns));

        var itemInsertIndex = category.Items.Count;
        for (int i = 0; i < category.Items.Count; i++)
        {
            if (category.Items[i].Order > itemOrder)
            {
                itemInsertIndex = i;
                break;
            }
        }
        category.Items.Insert(itemInsertIndex, item);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

