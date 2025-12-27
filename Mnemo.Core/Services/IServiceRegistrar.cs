namespace Mnemo.Core.Services;

/// <summary>
/// Abstraction for service registration to keep Core dependency-free.
/// </summary>
public interface IServiceRegistrar
{
    void AddSingleton<TService>() where TService : class;
    void AddSingleton<TService, TImplementation>() where TService : class where TImplementation : class, TService;
    void AddTransient<TService>() where TService : class;
    void AddTransient<TService, TImplementation>() where TService : class where TImplementation : class, TService;
}

