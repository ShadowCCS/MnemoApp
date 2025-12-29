using Mnemo.Core.Services;
using Mnemo.UI.Modules.Chat.ViewModels;

namespace Mnemo.UI.Modules.Chat;

public class ChatModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddTransient<ChatViewModel>();
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
        registry.RegisterRoute("chat", typeof(ChatViewModel));
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
        sidebarService.RegisterItem("AI Chat", "chat", "avares://Mnemo.UI/Icons/Tabler/Used/Filled/ghost-2.svg", "Ecosystem", 3, 0);
    }

    public void RegisterTools(IFunctionRegistry registry)
    {
        // No tools for chat yet
    }
}

