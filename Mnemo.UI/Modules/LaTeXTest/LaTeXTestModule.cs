using Mnemo.Core.Services;
using Mnemo.UI.Modules.LaTeXTest.ViewModels;

namespace Mnemo.UI.Modules.LaTeXTest;

public class LaTeXTestModule : IModule
{
    public void ConfigureServices(IServiceRegistrar services)
    {
        services.AddTransient<LaTeXTestViewModel>();
    }

    public void RegisterRoutes(INavigationRegistry registry)
    {
        registry.RegisterRoute("latex-test", typeof(LaTeXTestViewModel));
    }

    public void RegisterSidebarItems(ISidebarService sidebarService)
    {
        sidebarService.RegisterItem("LaTeX Test", "latex-test", "avares://Mnemo.UI/Icons/Tabler/Used/Filled/template.svg", "Library", 2);
    }

    public void RegisterTools(IFunctionRegistry registry)
    {
    }

    public void RegisterWidgets(IWidgetRegistry registry)
    {
    }
}
