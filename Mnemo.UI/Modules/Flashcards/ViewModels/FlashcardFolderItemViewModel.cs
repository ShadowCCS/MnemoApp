using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Mnemo.UI.Modules.Flashcards.ViewModels;

public partial class FlashcardFolderItemViewModel : ObservableObject
{
    public FlashcardFolderItemViewModel(string id, string name, string? parentId, int order, int depth)
    {
        Id = id;
        Name = name;
        ParentId = parentId;
        Order = order;
        Depth = depth;
    }

    public string Id { get; }

    [ObservableProperty]
    private string _name;

    public string? ParentId { get; private set; }

    public int Order { get; private set; }

    public int Depth { get; }

    public ObservableCollection<FlashcardFolderItemViewModel> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    public void UpdatePlacement(string? parentId, int order)
    {
        ParentId = parentId;
        Order = order;
    }
}
