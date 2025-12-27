using System.Collections.Generic;
using System.Windows.Input;

namespace Mnemo.Core.Models;

public enum DropdownType
{
    General,
    Action,
    Navigation,
    Context
}

public abstract class DropdownItemBase
{
    public string Text { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public ICommand? Command { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string ShortcutText { get; set; } = string.Empty;
}

public class DropdownOption : DropdownItemBase { }
public class DropdownHeader : DropdownItemBase { }
public class DropdownSeparator : DropdownItemBase { }

public interface IDropdownItemRegistry
{
    IEnumerable<DropdownItemBase> GetItems(DropdownType type, string? category = null);
}
