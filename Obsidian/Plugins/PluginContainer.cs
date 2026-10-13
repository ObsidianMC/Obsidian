using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Plugins.PluginProviders;
using Obsidian.Plugins.ServiceProviders;
using Obsidian.WorldData.Features;
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

    // How to undo what host services added on the plugin's behalf (see TryTrack), null once they've been undone.
    private List<Action>? hostAdditions = [];
    private readonly Lock hostAdditionsGate = new();

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

    /// <summary>
    /// The plugin's tag files and the tag entries it added in code, merged with vanilla's tags while it's loaded.
    /// </summary>
    internal List<TagFile> TagFiles { get; } = [];

    /// <summary>
    /// The plugin's biome, dimension type and damage type files, applied to their registries while it's loaded.
    /// </summary>
    internal ImmutableArray<CodecFile> CodecFiles { get; set; } = [];

    /// <summary>
    /// The plugin's feature and biome modifier files, applied to world generation while it's loaded.
    /// </summary>
    internal ImmutableArray<FeatureFile> FeatureFiles { get; set; } = [];

    /// <summary>
    /// The plugin's structure, structure set and template pool files, applied to world generation while it's loaded.
    /// </summary>
    internal ImmutableArray<StructureFile> StructureFiles { get; set; } = [];

    /// <summary>
    /// The plugin's structure templates, which its and others' pools can name while it's loaded.
    /// </summary>
    internal ImmutableArray<TemplateFile> StructureTemplates { get; set; } = [];

    /// <summary>
    /// The plugin's level stem files, whose dimensions are added while it's loaded and stay until the server stops.
    /// </summary>
    internal ImmutableArray<DimensionFile> DimensionFiles { get; set; } = [];

    /// <summary>
    /// The plugin's biome region files, which dimensions not generating yet take while it's loaded.
    /// </summary>
    internal ImmutableArray<BiomeRegionFile> BiomeRegionFiles { get; set; } = [];

    /// <summary>
    /// The biome regions the plugin registered in code, as the JSON of a region file, read after its files. Changed
    /// under <see cref="WorldgenTypes"/>' lock while it configures its registry.
    /// </summary>
    internal List<(string Identifier, byte[] Json)> RegisteredBiomeRegions { get; } = [];

    /// <summary>
    /// The plugin's noise settings, density function and noise files, which dimensions can use while it's loaded.
    /// </summary>
    internal ImmutableArray<NoiseFile> NoiseFiles { get; set; } = [];

    /// <summary>
    /// The worldgen types the plugin registered, which feature files can name while it's loaded.
    /// </summary>
    internal List<WorldgenTypeRegistration> WorldgenTypes { get; } = [];

    /// <summary>
    /// Whether the plugin is in <c>ConfigureRegistry</c>, the only time it may register worldgen types and
    /// generators. Changed and read under <see cref="WorldgenTypes"/>' lock.
    /// </summary>
    internal bool RegisteringWorldgenTypes { get; set; }

    public bool Loaded { get; internal set; }

    /// <summary>
    /// Creates the container of a plugin about to load, with a load context that resolves assemblies from the loaded
    /// plugins it depends on.
    /// </summary>
    /// <param name="archive">The plugin's archive.</param>
    /// <param name="loaded">The plugins already loaded.</param>
    /// <param name="side">The side this runs on; dependencies declared for the other side are ignored.</param>
    internal static PluginContainer Create(PluginArchive archive, IEnumerable<PluginContainer> loaded, PluginSide side)
    {
        var info = archive.Info;
        var dependencies = loaded
            .Where(plugin => info.Dependencies.Any(dependency => dependency.Id == plugin.Info.Id
                && PluginDependencyResolver.AppliesTo(dependency, side)))
            .ToImmutableArray();

        var container = new PluginContainer
        {
            Info = info,
            Archive = archive,
            LoadContext = new PluginLoadContext(info.AssemblyName),
            Dependencies = dependencies
        };

        container.LoadContext.Container = container;

        foreach (var dependency in dependencies)
            container.LoadContext.AddDependency(dependency.LoadContext);

        return container;
    }

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

    /// <summary>
    /// Starts a call into the plugin that its unloading makes, such as running its handlers of its menus closing, even
    /// though the plugin no longer takes other calls.
    /// </summary>
    /// <returns>The call, to dispose when it ends.</returns>
    internal PluginCall EnterUnloading()
    {
        Interlocked.Increment(ref this.activeCalls);

        return PluginCall.Start(this);
    }

    // Ends a call started with TryEnter or EnterUnloading; PluginCall.Dispose calls it.
    internal void Exit() => Interlocked.Decrement(ref this.activeCalls);

    /// <summary>
    /// Keeps the plugin from being released, like a call, for something of the plugin's that outlives a call, such as
    /// a level's generator. Unlike a call, it doesn't make the current flow count as inside the plugin.
    /// </summary>
    /// <returns>The hold, to dispose once it's no longer needed, or null once the plugin is unloading.</returns>
    internal IDisposable? TryHold()
    {
        Interlocked.Increment(ref this.activeCalls);

        if (!this.closing)
            return new Hold(this);

        Interlocked.Decrement(ref this.activeCalls);
        return null;
    }

    /// <summary>
    /// Keeps the plugin from being released, like <see cref="TryHold"/>, for work its unloading leaves to finish
    /// later, such as saving the state of its structures once their placements end.
    /// </summary>
    /// <returns>The hold, to dispose once the work is done.</returns>
    internal IDisposable HoldUnloading()
    {
        Interlocked.Increment(ref this.activeCalls);

        return new Hold(this);
    }

    /// <summary>
    /// Creates a scope of the plugin's services, still falling back to the server's, for something of the plugin's
    /// that lives longer than a call; dispose it once that's done, before the plugin is released.
    /// </summary>
    internal PluginServiceProvider CreateServiceScope() =>
        (this.ownServices ?? throw new InvalidOperationException($"{this.Info.Name} has no services."))
            .CreateScope();

    /// <summary>
    /// Records how to undo something a host service added on the plugin's behalf, like a recipe or a scoreboard, which
    /// unloading the plugin undoes (see <see cref="UndoHostAdditions"/>).
    /// </summary>
    /// <param name="undo">Removes the addition; it may run on any thread.</param>
    /// <returns>
    /// False once the plugin's additions were undone: then undo this one too and refuse it, as the plugin unloaded.
    /// </returns>
    internal bool TryTrack(Action undo)
    {
        lock (this.hostAdditionsGate)
        {
            if (this.hostAdditions is null)
                return false;

            this.hostAdditions.Add(undo);
            return true;
        }
    }

    /// <summary>
    /// Tracks something a host service just added for <paramref name="caller"/>, when that's a plugin's code, so that
    /// unloading the plugin undoes it; the server's own additions aren't tracked.
    /// </summary>
    /// <param name="caller">The assembly that called the host service.</param>
    /// <param name="undo">Removes the addition.</param>
    /// <exception cref="InvalidOperationException">
    /// The plugin is unloading; the addition was undone.
    /// </exception>
    internal static void TrackAddition(Assembly caller, Action undo)
    {
        if (PluginLoadContext.OwnerOf(caller) is not PluginContainer owner || owner.TryTrack(undo))
            return;

        undo();
        throw new InvalidOperationException($"{owner.Info.Name} is unloading.");
    }

    /// <summary>
    /// Undoes what host services added on the plugin's behalf, as it unloads, and refuses later additions.
    /// </summary>
    /// <returns>The failures of undoing, to log; the other additions are still undone.</returns>
    internal List<Exception> UndoHostAdditions()
    {
        List<Action>? additions;
        lock (this.hostAdditionsGate)
        {
            additions = this.hostAdditions;
            this.hostAdditions = null;
        }

        var failures = new List<Exception>();

        foreach (var undo in additions ?? [])
        {
            try
            {
                undo();
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        return failures;
    }

    /// <summary>
    /// Refuses new calls into the plugin.
    /// </summary>
    internal void Close() => this.closing = true;

    /// <summary>
    /// Whether the plugin started unloading.
    /// </summary>
    internal bool IsClosing => this.closing;

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
                this.WorldgenTypes.Clear();
                this.PluginAssembly = null;
                this.LoadContext = null;
                this.Plugin = null;
            }
        }
    }

    // Ends a hold started with TryHold, once.
    private sealed class Hold(PluginContainer plugin) : IDisposable
    {
        private PluginContainer? plugin = plugin;

        public void Dispose() => Interlocked.Exchange(ref this.plugin, null)?.Exit();
    }

    // The container's scope, disposed with the container rather than by whoever holds it.
    private sealed class Scope(PluginServiceProvider provider) : IServiceScope
    {
        public IServiceProvider ServiceProvider => provider;

        public void Dispose() { }
    }
}
