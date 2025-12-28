using System.Collections.ObjectModel;
using Mnemo.Core.Models;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Components;

public class TopbarViewModel : ViewModelBase
{
    public ObservableCollection<TopbarButtonModel> Buttons { get; } = new();
}



