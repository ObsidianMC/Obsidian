using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Plugins.PluginProviders;
using Obsidian.Plugins.ServiceProviders;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Obsidian.Plugins;

/// <summary>
/// A loaded plugin: its archive, the load context its assemblies live in, and its services.
/// </summary>
/// <remarks>
/// Keeping a reference to a container, its plugin or anything from its assembly outside the plugin manager stops the
/// plugin from unloading.
/// </remarks>
public sealed class PluginContainer : IDisposable, IPluginContainer
{
    private ServiceProvider? ownServices;

    public IServiceScope ServiceScope { get; private set; } = default!;

    /// <summary>
    /// The plugin's services, falling back to those of its dependencies and the server.
    /// </summary>
    public IServiceProvider ServiceProvider { get; private set; } = default!;

    public required PluginInfo Info { get; init; }

    public required PluginArchive Archive { get; init; }

    public PluginBase? Plugin { get; internal set; }

    [AllowNull]
    public PluginLoadContext LoadContext { get; internal set; } = default!;

    [AllowNull]
    public Assembly PluginAssembly { get; internal set; } = default!;

    /// <summary>
    /// The loaded plugins this one depends on, which its load context resolves assemblies from.
    /// </summary>
    public ImmutableArray<PluginContainer> Dependencies { get; init; } = [];

    public required string Source { get; set; }
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
    internal void SetServices(IServiceCollection services, IServiceProvider serverProvider)
    {
        this.ownServices = services.BuildServiceProvider(validateScopes: true);

        IReadOnlyList<IServiceProvider> fallbacks =
        [
            .. this.Dependencies.Select(dependency => dependency.ServiceProvider),
            serverProvider
        ];
        var provider = new PluginServiceProvider(this.ownServices, fallbacks);

        this.ServiceProvider = provider;
        this.ServiceScope = provider.CreateScope();
    }

    /// <summary>
    /// Inject the scoped services into the plugin, or into a module of it.
    /// </summary>
    public void InjectServices(ILogger? logger, object? target = null) =>
        PluginServiceHandler.InjectServices(this.ServiceScope.ServiceProvider, target ?? this.Plugin, logger);

    ///<inheritdoc/>
    public byte[]? GetFileData(string fileName) => this.Archive.Read(fileName);

    public void Dispose()
    {
        this.ServiceScope?.Dispose();
        this.ownServices?.Dispose();

        this.ServiceScope = null!;
        this.ServiceProvider = null!;
        this.ownServices = null;
        this.PluginAssembly = null;
        this.LoadContext = null;
        this.Plugin = null;
    }
}
