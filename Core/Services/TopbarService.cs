using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;

namespace MnemoApp.Core.Services
{
    public sealed class TopbarService : ITopbarService
    {
        private readonly ObservableCollection<ITopbarItem> _items = new();
        private readonly ReadOnlyObservableCollection<ITopbarItem> _roItems;

        public TopbarService()
        {
            _roItems = new ReadOnlyObservableCollection<ITopbarItem>(_items);
        }

        public ReadOnlyObservableCollection<ITopbarItem> Items => _roItems;

        public Guid AddButton(TopbarButtonModel model)
        {
            InsertSorted(model);
            return model.Id;
        }

        public Guid AddCustom(Control control, int order = 0)
        {
            var model = new TopbarCustomModel(control, order);
            InsertSorted(model);
            return model.Id;
        }

        public Guid AddSeparator(int order = 0, double height = 24, double thickness = 1)
        {
            var sep = new TopbarSeparatorModel { Order = order, Height = height, Thickness = thickness };
            InsertSorted(sep);
            return sep.Id;
        }

        public bool Remove(Guid id)
        {
            var existing = _items.FirstOrDefault(i => i.Id == id);
            if (existing == null) return false;
            _items.Remove(existing);
            return true;
        }

        public bool SetNotification(Guid id, bool notification)
        {
            if (_items.FirstOrDefault(i => i.Id == id) is TopbarButtonModel btn)
            {
                btn.Notification = notification;
                // replace to notify collection change for mutable property
                var idx = _items.IndexOf(btn);
                if (idx >= 0)
                {
                    _items[idx] = btn;
                }
                return true;
            }
            return false;
        }

        public void Clear() => _items.Clear();

        private void InsertSorted(ITopbarItem item)
        {
            var index = _items.TakeWhile(i => i.Order <= item.Order).Count();
            _items.Insert(index, item);
        }
    }
}


