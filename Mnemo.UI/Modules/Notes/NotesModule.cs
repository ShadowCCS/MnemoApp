using System;
using Mnemo.Core.Models.Keybinds;
using Mnemo.Core.Services;
using Mnemo.Core.Services.Keybinds;
using Mnemo.Core.Services.Search;
using Microsoft.Extensions.DependencyInjection;
using Mnemo.Infrastructure.Services;
using Mnemo.Infrastructure.Services.Notes;
using Mnemo.Infrastructure.Services.Search;
using Mnemo.Infrastructure.Services.Tools;
using Mnemo.UI.Services;

namespace Mnemo.UI.Modules.Notes;

public class NotesModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddTransient<ViewModels.NotesViewModel>();
        services.AddSingleton<ISearchProvider, NotesSearchProvider>();
        services.AddSingleton<INotesEditorViewDispatch, NotesEditorViewDispatch>();
    }

    public void RegisterTranslationSources(ITranslationSourceRegistry registry)
    {
        var assembly = typeof(NotesModule).Assembly;
        registry.Add(new EmbeddedJsonTranslationSource(assembly, "Mnemo.UI.Modules.Notes.Translations"));
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

    public void RegisterWidgets(IWidgetRegistry registry, IServiceProvider services)
    {
    }

    public void RegisterKeybindManifest(IKeybindManifestRegistry registry)
    {
        registry.Register(new KeybindActionDefinition
        {
            ActionId = "editor.reset-view",
            Namespace = "editor",
            Scope = KeybindScope.Local,
            Module = "editor",
            DisplayLabelKey = "editor.reset-view",
            DisplayDescriptionKey = "editor.reset-view.description",
            DisplayCategoryKey = "category.view",
            Bindings =
            [
                new KeybindBindingEntry
                {
                    Kind = KeybindBindingKind.Chord,
                    Chord = CanonicalKeyGestureCodec.ParseChord("Primary+D0")
                }
            ]
        });
    }
}
