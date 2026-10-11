using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Plugins.PluginProviders;
using Obsidian.Plugins.ServiceProviders;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;

namespace Obsidian.Plugins;

/// <summary>
/// A loaded plugin: its archive, the load context its assemblies live in, and its services.
/// </summary>
/// <remarks>
/// Keeping a reference to a container, its plugin or anything from its assembly outside the plugin manager stops the
/// plugin from unloading.
/// </remarks>
public sealed class PluginContainer : IPluginContainer, IAsyncDisposable
{
    private PluginServiceProvider? ownServices;
    private PluginServiceProvider? scope;

    // Calls into the plugin that are running (see TryEnter), and whether it is unloading, which refuses new ones.
    private int activeCalls;
    private volatile bool closing;

    public IServiceScope ServiceScope { get; private set; } = default!;

    /// <summary>
    /// The plugin's services, falling back to the server's.
    /// </summary>
    public IServiceProvider ServiceProvider => this.ownServices!;

    public required PluginInfo Info { get; init; }

    public required PluginArchive Archive { get; init; }

    /// <summary>
    /// The plugin's file.
    /// </summary>
    public string Source => this.Archive.Source;

    public PluginBase? Plugin { get; internal set; }

    [AllowNull]
    public PluginLoadContext LoadContext { get; internal set; } = default!;

    [AllowNull]
    public Assembly PluginAssembly { get; internal set; } = default!;

    /// <summary>
    /// The loaded plugins this one depends on, which its load context resolves assemblies from.
    /// </summary>
    public ImmutableArray<PluginContainer> Dependencies { get; init; } = [];

    /// <summary>
    /// The singleton services the plugin registered, which the plugins that depend on it can use too.
    /// </summary>
    internal ImmutableArray<Type> ExportedSingletons { get; private set; } = [];

    public bool ValidSignature => this.Archive.ValidSignature;
    public bool Loaded { get; internal set; }

    internal void Initialize()
    {
        this.Plugin!.Container = this;
        this.Plugin!.Info = this.Info;
    }

    /// <summary>
    /// Builds the plugin's services once it registered them.
    /// </summary>
    /// <param name="services">The plugin's services, starting with <paramref name="registeredFrom"/> base ones.</param>
    /// <param name="registeredFrom">The number of base services, which come before the plugin's own.</param>
    /// <param name="serverProvider">The server's services.</param>
    internal void SetServices(IServiceCollection services, int registeredFrom, IServiceProvider serverProvider)
    {
        this.ExportedSingletons =
        [
            .. services
                .Skip(registeredFrom)
                .Where(descriptor => descriptor.Lifetime == ServiceLifetime.Singleton
                    && !descriptor.IsKeyedService
                    && !descriptor.ServiceType.IsGenericTypeDefinition)
                .Select(descriptor => descriptor.ServiceType)
                .Distinct()
        ];

        var provider = services.BuildServiceProvider(validateScopes: true);

        this.ownServices = new PluginServiceProvider(provider, serverProvider, provider);
        this.scope = this.ownServices.CreateScope();
        this.ServiceScope = new Scope(this.scope);
    }

    /// <summary>
    /// Starts a call into the plugin, e.g. running one of its event handlers, which releasing the plugin waits for.
    /// </summary>
    /// <returns>The call, to dispose when it ends, or null once the plugin is unloading: then don't call it.</returns>
    internal PluginCall? TryEnter()
    {
        Interlocked.Increment(ref this.activeCalls);

        if (!this.closing)
            return PluginCall.Start(this);

        Interlocked.Decrement(ref this.activeCalls);
        return null;
    }

    // Ends a call started with TryEnter; PluginCall.Dispose calls it.
    internal void Exit() => Interlocked.Decrement(ref this.activeCalls);

    /// <summary>
    /// Refuses new calls into the plugin.
    /// </summary>
    internal void Close() => this.closing = true;

    /// <summary>
    /// Waits until no call into the plugin runs.
    /// </summary>
    internal async Task WaitForCallsAsync()
    {
        while (Volatile.Read(ref this.activeCalls) > 0)
            await Task.Delay(10);
    }

    /// <summary>
    /// Inject the scoped services into the plugin, or into a module of it.
    /// </summary>
    public void InjectServices(ILogger? logger, object? target = null) =>
        PluginServiceHandler.InjectServices(this.ServiceScope.ServiceProvider, target ?? this.Plugin, logger);

    ///<inheritdoc/>
    public byte[]? GetFileData(string fileName) => this.Archive.Read(fileName);

    /// <summary>
    /// Disposes the plugin's services and drops every reference into its load context, even if disposing a service
    /// fails, so that the load context can still unload.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (this.scope is not null)
                await this.scope.DisposeAsync();
        }
        finally
        {
            try
            {
                if (this.ownServices is not null)
                    await this.ownServices.DisposeAsync();
            }
            finally
            {
                this.scope = null;
                this.ownServices = null;
                this.ServiceScope = null!;
                this.ExportedSingletons = [];
                this.PluginAssembly = null;
                this.LoadContext = null;
                this.Plugin = null;
            }
        }
    }

    // The container's scope, disposed with the container rather than by whoever holds it.
    private sealed class Scope(PluginServiceProvider provider) : IServiceScope
    {
        public IServiceProvider ServiceProvider => provider;

        public void Dispose() { }
    }
}
