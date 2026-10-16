using Microsoft.Extensions.DependencyInjection;

namespace Obsidian.Plugins.ServiceProviders;

/// <summary>
/// Resolves a plugin's services: the ones it registered in <see cref="API.Plugins.PluginBase.ConfigureServices"/> (with
/// the singletons of the plugins it depends on) first, then the server's.
/// </summary>
/// <remarks>
/// <para>
/// Each plugin has a provider of its own, rather than all plugins sharing one, so that unloading a plugin disposes its
/// services and leaves nothing that refers to its assembly, which could then not unload.
/// </para>
/// <para>
/// A service provider caches every type it is asked for, even one it doesn't have. So the server's provider, which
/// outlives every plugin, is never asked for a type from a plugin's assembly, like the plugin's own services or
/// <c>ILogger&lt;MyPlugin&gt;</c>.
/// </para>
/// </remarks>
/// <param name="own">The plugin's own services, or null to only resolve the server's.</param>
/// <param name="server">The server's services.</param>
/// <param name="lifetime">What disposing this provider disposes: the plugin's provider or one of its scopes.</param>
internal sealed class PluginServiceProvider(IServiceProvider? own, IServiceProvider server,
    IAsyncDisposable? lifetime = null) : IServiceProvider, IAsyncDisposable
{
    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider))
            return this;

        if (own?.GetService(serviceType) is object service)
            return service;

        return IsCollectible(serviceType) ? null : server.GetService(serviceType);
    }

    /// <summary>
    /// Creates a scope of the plugin's own services that still falls back to the server's.
    /// </summary>
    public PluginServiceProvider CreateScope()
    {
        var scope = (own ?? throw new InvalidOperationException("Only a plugin's own services have scopes."))
            .CreateAsyncScope();

        return new PluginServiceProvider(scope.ServiceProvider, server, scope);
    }

    public ValueTask DisposeAsync() => lifetime?.DisposeAsync() ?? default;

    /// <summary>
    /// Whether a type belongs to a collectible (plugin) load context, or is built from one that does.
    /// </summary>
    public static bool IsCollectible(Type type) =>
        type.Assembly.IsCollectible
        || (type.HasElementType && IsCollectible(type.GetElementType()!))
        || (type.IsGenericType && type.GenericTypeArguments.Any(IsCollectible));
}
