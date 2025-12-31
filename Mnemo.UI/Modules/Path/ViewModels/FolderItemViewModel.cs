using System.Collections.ObjectModel;

namespace Mnemo.UI.Modules.Path.ViewModels;

public class FolderItemViewModel : PathBaseViewModel
{
    public override bool IsFolder => true;
    public ObservableCollection<PathBaseViewModel> Children { get; } = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}