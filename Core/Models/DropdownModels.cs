using System.Windows.Input;

namespace MnemoApp.Core.Models
{
    public abstract class DropdownItemBase
    {
        public string? Id { get; set; }
        public int Order { get; set; } = 0;
    }

    public class DropdownOption : DropdownItemBase
    {
        public string Text { get; set; } = string.Empty;
        public object? Icon { get; set; }
        public string? ShortcutText { get; set; }
        public ICommand? Command { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string? Category { get; set; }
    }

    public class DropdownHeader : DropdownItemBase
    {
        public string Text { get; set; } = string.Empty;
    }

    public class DropdownSeparator : DropdownItemBase
    {
    }

    public enum DropdownType
    {
        Options,
        Notifications,
        Context
    }
}
