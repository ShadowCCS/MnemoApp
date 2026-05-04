using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Notes.ViewModels;

public abstract class NoteBreadcrumbPieceBase : ViewModelBase
{
    public bool ShowLeadingSeparator { get; init; }
}
