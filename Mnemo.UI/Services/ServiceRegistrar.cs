using Microsoft.Extensions.DependencyInjection;
using Mnemo.Core.Services;

namespace Mnemo.UI.Services;

public class ServiceRegistrar : IServiceRegistrar
{
    private readonly IServiceCollection _services;

    public ServiceRegistrar(IServiceCollection services)
    {
        _services = services;
    }

    public void AddSingleton<TService>() where TService : class
    {
        _services.AddSingleton<TService>();
    }

    public void AddSingleton<TService, TImplementation>() 
        where TService : class 
        where TImplementation : class, TService
    {
        _services.AddSingleton<TService, TImplementation>();
    }

    public void AddTransient<TService>() where TService : class
    {
        _services.AddTransient<TService>();
    }

    public void AddTransient<TService, TImplementation>() 
        where TService : class 
        where TImplementation : class, TService
    {
        _services.AddTransient<TService, TImplementation>();
    }
}

