using System.Collections.Generic;
using Mnemo.UI.ViewModels;

namespace Mnemo.UI.Modules.Notes.ViewModels;

public sealed class NoteBreadcrumbEllipsisPieceVm : NoteBreadcrumbPieceBase
{
    public NoteBreadcrumbEllipsisPieceVm(List<NoteBreadcrumbHiddenItemVm> hiddenItems)
    {
        HiddenItems = hiddenItems;
    }

    public List<NoteBreadcrumbHiddenItemVm> HiddenItems { get; }
}
