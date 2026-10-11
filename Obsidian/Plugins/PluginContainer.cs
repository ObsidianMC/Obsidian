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

    // The plugins the current async flow is calling into, so that a plugin unloading itself from one of its own
    // handlers (a reload command, say) doesn't wait for that handler.
    private static readonly AsyncLocal<ImmutableList<PluginContainer>?> currentCalls = new();

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

        // The plugin's services are built with the server's available to their constructors too. The plugin's own
        // provider can't fall back to another, so they are created through one that does.
        for (var i = registeredFrom; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.ImplementationType is not Type implementation
                || descriptor.IsKeyedService
                || implementation.IsGenericTypeDefinition)
                continue;

            services[i] = ServiceDescriptor.Describe(
                descriptor.ServiceType,
                provider => ActivatorUtilities.CreateInstance(
                    new PluginServiceProvider(provider, serverProvider),
                    implementation),
                descriptor.Lifetime);
        }

        var provider = services.BuildServiceProvider(validateScopes: true);

        this.ownServices = new PluginServiceProvider(provider, serverProvider, provider);
        this.scope = this.ownServices.CreateScope();
        this.ServiceScope = new Scope(this.scope);
    }

    /// <summary>
    /// Starts a call into the plugin, e.g. running one of its event handlers, which unloading waits for.
    /// </summary>
    /// <returns>Whether the call may go ahead; it may not once the plugin is unloading. Then don't call
    /// <see cref="Exit"/>.</returns>
    internal bool TryEnter()
    {
        Interlocked.Increment(ref this.activeCalls);

        if (this.closing)
        {
            Interlocked.Decrement(ref this.activeCalls);
            return false;
        }

        currentCalls.Value = (currentCalls.Value ?? []).Add(this);
        return true;
    }

    /// <summary>
    /// Ends a call started with <see cref="TryEnter"/>, on the same async flow.
    /// </summary>
    internal void Exit()
    {
        currentCalls.Value = currentCalls.Value?.Remove(this);
        Interlocked.Decrement(ref this.activeCalls);
    }

    /// <summary>
    /// Refuses new calls into the plugin and waits for the running ones to end, for at most <paramref name="timeout"/>
    /// (or <see cref="Timeout.InfiniteTimeSpan"/>). Calls the caller itself is inside of don't count.
    /// </summary>
    /// <returns>Whether every call ended in time.</returns>
    internal async Task<bool> CloseAsync(TimeSpan timeout)
    {
        this.closing = true;

        var own = currentCalls.Value?.Count(call => call == this) ?? 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (Volatile.Read(ref this.activeCalls) > own)
        {
            if (timeout != Timeout.InfiniteTimeSpan && stopwatch.Elapsed >= timeout)
                return false;

            await Task.Delay(10);
        }

        return true;
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
