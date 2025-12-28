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
        // Using route-square for now, or another icon if found. 
        // Based on existing icons, route-square is a good fit for a "hub" or "ai" interaction if specialized icon is missing.
        sidebarService.RegisterItem("AI Chat", "chat", "avares://Mnemo.UI/Icons/Tabler/Used/Outlined/file-text.svg", "Ecosystem", 3, 0);
    }

    public void RegisterTools(IFunctionRegistry registry)
    {
        // No tools for chat yet
    }
}

