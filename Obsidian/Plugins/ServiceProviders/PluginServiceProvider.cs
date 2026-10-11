using Microsoft.Extensions.DependencyInjection;

namespace Obsidian.Plugins.ServiceProviders;

/// <summary>
/// Resolves a plugin's services: the ones it registered in <see cref="API.Plugins.PluginBase.ConfigureServices"/>
/// first, then those of the plugins it depends on, then the server's.
/// </summary>
/// <remarks>
/// Each plugin has a provider of its own, rather than all plugins sharing one, so that unloading a plugin disposes its
/// services and leaves nothing that refers to its assembly, which could then not unload.
/// </remarks>
internal sealed class PluginServiceProvider(IServiceProvider own, IReadOnlyList<IServiceProvider> fallbacks)
    : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider))
            return this;

        if (own.GetService(serviceType) is object service)
            return service;

        foreach (var fallback in fallbacks)
        {
            if (fallback.GetService(serviceType) is object fallbackService)
                return fallbackService;
        }

        return null;
    }

    /// <summary>
    /// Creates a scope of the plugin's own services that still falls back to its dependencies' and the server's.
    /// </summary>
    public IServiceScope CreateScope() => new Scope(own.CreateScope(), fallbacks);

    private sealed class Scope(IServiceScope scope, IReadOnlyList<IServiceProvider> fallbacks) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new PluginServiceProvider(scope.ServiceProvider, fallbacks);

        public void Dispose() => scope.Dispose();
    }
}
