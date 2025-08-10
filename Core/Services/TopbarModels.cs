using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;

namespace MnemoApp.Core.Services
{
    public enum TopbarItemType
    {
        Button,
        Custom
    }

    public interface ITopbarItem
    {
        Guid Id { get; }
        TopbarItemType Type { get; }
        int Order { get; }
    }

    public sealed class TopbarButtonModel : ITopbarItem
    {
        public Guid Id { get; } = Guid.NewGuid();
        public TopbarItemType Type => TopbarItemType.Button;
        public int Order { get; init; }

        public string IconPath { get; init; } = string.Empty; // avares:// path
        public object? Stroke { get; init; } // Brush or Binding-friendly object
        public bool Notification { get; set; }
        public string? ToolTip { get; init; }
        public ICommand? Command { get; init; }
    }

    public sealed class TopbarCustomModel : ITopbarItem
    {
        public Guid Id { get; } = Guid.NewGuid();
        public TopbarItemType Type => TopbarItemType.Custom;
        public int Order { get; init; }

        public Control Content { get; }

        public TopbarCustomModel(Control content, int order = 0)
        {
            Content = content;
            Order = order;
        }
    }

    public interface ITopbarService
    {
        ReadOnlyObservableCollection<ITopbarItem> Items { get; }
        Guid AddButton(TopbarButtonModel model);
        Guid AddCustom(Control control, int order = 0);
        bool Remove(Guid id);
        bool SetNotification(Guid id, bool notification);
        void Clear();
    }
}


