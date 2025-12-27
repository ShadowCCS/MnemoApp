using Mnemo.Core.Services;
using Mnemo.UI.Modules.Notes.ViewModels;

namespace Mnemo.UI.Modules.Notes;

public class NotesModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddTransient<NotesViewModel>();
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
        registry.RegisterRoute("notes", typeof(NotesViewModel));
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
        sidebarService.RegisterItem("Notes", "notes", "avares://Mnemo.UI/Icons/Tabler/Used/Filled/book.svg", "Library");
    }

    public void RegisterTools(IFunctionRegistry registry)
    {
        // Tools for AI to interact with notes could be registered here
    }
}


