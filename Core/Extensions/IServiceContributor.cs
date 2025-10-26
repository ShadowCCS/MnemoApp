using Microsoft.Extensions.DependencyInjection;

namespace MnemoApp.Core.Extensions
{
    /// <summary>
    /// Optional interface for extensions that contribute services to DI container
    /// </summary>
    public interface IServiceContributor
    {
        /// <summary>
        /// Register services in the DI container
        /// Called during application startup before service provider is built
        /// </summary>
        void RegisterServices(IServiceCollection services);
    }
}

