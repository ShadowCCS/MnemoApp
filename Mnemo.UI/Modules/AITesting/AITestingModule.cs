using Mnemo.Core.Services;
using Mnemo.UI.Modules.AITesting.ViewModels;

namespace Mnemo.UI.Modules.AITesting;

public class AITestingModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddTransient<AITestingViewModel>();
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
        registry.RegisterRoute("ai-testing", typeof(AITestingViewModel));
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
        sidebarService.RegisterItem("AI Testing", "ai-testing", "avares://Mnemo.UI/Icons/Tabler/Used/Filled/chart-bubble.svg", "Test AI features", 99, 1);
    }

    public void RegisterTools(IFunctionRegistry registry)
    {
    }
}

