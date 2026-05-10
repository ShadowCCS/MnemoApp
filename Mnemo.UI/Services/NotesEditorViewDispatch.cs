using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using Mnemo.Core.Services;
using Mnemo.UI.Modules.Notes.ViewModels;
using Mnemo.UI.Modules.Notes.Views;

namespace Mnemo.UI.Services;

public sealed class NotesEditorViewDispatch(INavigationService navigation) : INotesEditorViewDispatch
{
    public bool TryResetEditorView()
    {
        if (!string.Equals(navigation.CurrentRoute, "notes", StringComparison.Ordinal))
            return false;
        if (navigation.CurrentViewModel is not NotesViewModel)
            return false;
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime life)
            return false;
        if (life.MainWindow is not { } window)
            return false;
        if (window.GetVisualDescendants().OfType<NotesView>().FirstOrDefault() is not { } notesView)
            return false;
        notesView.ResetEditorView();
        return true;
    }
}
