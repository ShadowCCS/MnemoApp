using Mnemo.Core.Services;
using Mnemo.UI.Modules.Chat.ViewModels;

namespace Mnemo.UI.Modules.Chat;

public class ChatModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddTransient<ChatViewModel>();
    }

    public void RegisterTranslationSources(ITranslationSourceRegistry registry)
    {
        // No module translations yet
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
        registry.RegisterRoute("chat", typeof(ChatViewModel));
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
        sidebarService.RegisterItem("AIChat", "chat", "avares://Mnemo.UI/Icons/Tabler/Used/Filled/sparkles-2.svg", "Ecosystem", 3, 0);
    }

    public void RegisterTools(IFunctionRegistry registry, IServiceProvider services)
    {
        // No tools for chat yet
    }

    public void RegisterWidgets(IWidgetRegistry registry)
    {
        // No widgets for chat
    }
}

