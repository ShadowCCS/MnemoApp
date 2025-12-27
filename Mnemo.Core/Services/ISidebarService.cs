using System.Collections.ObjectModel;
using Mnemo.Core.Models;

namespace Mnemo.Core.Services;

public interface ISidebarService
{
    ObservableCollection<SidebarCategory> Categories { get; }
    void RegisterItem(string label, string route, string icon, string category = "General", int? categoryOrder = null);
    bool IsCollapsed { get; set; }
}
