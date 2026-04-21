using System;
using Mnemo.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Infrastructure.Services.Notes;
using Mnemo.Infrastructure.Services.Tools;

namespace Mnemo.UI.Modules.Notes;

public class NotesModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddTransient<ViewModels.NotesViewModel>();
    }

    public void RegisterTranslationSources(ITranslationSourceRegistry registry)
    {
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
        registry.RegisterRoute("notes", typeof(ViewModels.NotesViewModel));
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
        sidebarService.RegisterItem("Notes", "notes", "avares://Mnemo.UI/Icons/Sidebar/notes.svg", "Library", 1, int.MaxValue);
    }

    public void RegisterTools(IFunctionRegistry registry, IServiceProvider services)
    {
        var notesToolService = services.GetRequiredService<NotesToolService>();
        NotesToolRegistrar.Register(registry, notesToolService);
    }

    public void RegisterWidgets(IWidgetRegistry registry)
    {
    }
}
