using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using Obsidian.API.Plugins;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Plugins.Archives;
using Obsidian.Plugins.PluginProviders;
using Obsidian.Plugins.ServiceProviders;
using Obsidian.Services;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Obsidian.Plugins;

/// <summary>
/// Loads, reloads and unloads plugins.
/// </summary>
/// <remarks>
/// <para>
/// At startup every plugin in the plugins folder loads, in dependency order, before the worlds do. Afterwards the
/// folder is watched: a changed file reloads its plugin, and a deleted one unloads it. A plugin's dependents reload
/// with it, since they use its types. Plugins that can't load yet (a required dependency is missing, say) wait and are
/// tried again whenever the loaded plugins change.
/// </para>
/// <para>
/// Everything a plugin registers (commands, events, payload channels, services) is removed when it unloads, with the
/// entities and server players it spawned, so that its collectible load context can unload too.
/// </para>
/// </remarks>
public sealed partial class PluginManager : IAsyncDisposable
{
    // How long unloading, and stopping, wait for plugins' running handlers to finish before going on without them.
    private static readonly TimeSpan drainTimeout = TimeSpan.FromSeconds(5);

    // Set while a change runs on this async flow, to catch a plugin changing plugins from its own lifecycle callback,
    // which would wait for itself.
    private static readonly AsyncLocal<PluginManager?> changing = new();

    /// <summary>
    /// The pass creating plugins' dimensions on this async flow, whose generation runs plugins' code: a change from
    /// there would wait for the pass it runs in, which may wait for a change. Tasks the pass starts inherit it, so it
    /// only counts while <see cref="DimensionPass.Active"/>: a task outliving the pass may change plugins.
    /// </summary>
    internal static readonly AsyncLocal<DimensionPass?> creatingDimensions = new();

    /// <summary>
    /// A pass creating plugins' dimensions (see <see cref="creatingDimensions"/>).
    /// </summary>
    internal sealed class DimensionPass
    {
        private volatile bool active = true;

        public bool Active => this.active;

        public void End() => this.active = false;
    }

    internal readonly ILogger logger;
    private readonly IConfiguration configuration;
    internal readonly IServer server;

    private readonly IServiceProvider serverProvider;
    private readonly CommandHandler commandHandler;
    private readonly EventDispatcher eventDispatcher;
    private readonly PluginStartup? startup;

    // Loading and unloading change the plugin list and what is registered, so they happen one at a time.
    private readonly SemaphoreSlim changes = new(1, 1);

    // Unloaded plugins waiting for their running calls to end before they are released (see UnloadAsync).
    private readonly ConcurrentDictionary<PluginContainer, Task> releases = new();

    private ImmutableArray<PluginContainer> plugins = [];
    private ImmutableArray<PluginArchive> stagedPlugins = [];

    // The server's data packs, read once at startup; their files apply after every plugin's (see DataPack).
    private ImmutableArray<DataPack> dataPacks = [];

    // Vanilla's dimension types whose overrides are left out, as a vanilla dimension can't generate in their build
    // range (see SettleDimensionTypes).
    private ImmutableHashSet<string> keptDimensionTypes = [];

    // Whether vanilla's dimension types' build ranges are settled: the worlds create their levels with them once
    // startup has read every plugin and data pack. Until then, startup goes on.
    private bool dimensionTypesSettled;

    // Level stems startup couldn't read as it read their plugin or data pack. What they name may come with a later
    // plugin or pack, or change as dimension types settle, so they're read again once every plugin and pack is read,
    // and once more when types settled (see SettleDimensionTypes), and only left out, logged, if they still can't be.
    private readonly List<StartupStem> startupStems = [];

    private bool serverReady;
    private bool disposed;

    /// <summary>
    /// The loaded plugins, in load order.
    /// <br/><b>Important note:</b> keeping references to plugin containers outside this class will make them
    /// unloadable.
    /// </summary>
    public IReadOnlyList<PluginContainer> Plugins => this.plugins;

    /// <summary>
    /// Plugins that can't load yet, e.g. because a required dependency is missing.
    /// </summary>
    public IReadOnlyList<PluginArchive> StagedPlugins => this.stagedPlugins;

    /// <summary>
    /// The side plugins run on here. Plugins only for the other side are skipped.
    /// </summary>
    public PluginSide Side => PluginSide.Server;

    /// <summary>
    /// Utility class, responding to file changes inside watched directories.
    /// </summary>
    public DirectoryWatcher DirectoryWatcher { get; } = new();

    public PluginManager(IServer server, IServiceProvider serverProvider, EventDispatcher eventDispatcher,
        CommandHandler commandHandler, ILogger<PluginManager> logger, IConfiguration configuration)
    {
        this.server = server;
        this.commandHandler = commandHandler;
        this.eventDispatcher = eventDispatcher;
        this.logger = logger;
        this.configuration = configuration;
        this.serverProvider = serverProvider;
        this.startup = serverProvider.GetService<PluginStartup>();
        API.Data.PluginDataBinding.HandlerFailed = (key, exception) =>
            logger.LogError(exception, "A change handler of {Attachment} failed.", key);

        DirectoryWatcher.Filters = [".obby"];
        DirectoryWatcher.FileChanged += path => _ = this.LoadPluginAsync(path);
        DirectoryWatcher.FileRenamed += (oldPath, newPath) => _ = this.RenameAsync(oldPath, newPath);
        DirectoryWatcher.FileDeleted += path => _ = this.UnloadPluginAsync(path);
    }

    /// <summary>
    /// Loads every plugin in the plugins folder and starts watching it.
    /// </summary>
    public async Task LoadPluginsAsync()
    {
        try
        {
            await this.RunChangeAsync(async () =>
            {
                // Obsidian's convention (c:) tags are there with no plugins too; each plugin applies the tags again.
                this.ApplyTags();

                var archives = new List<PluginArchive>();
                var files = Directory.GetFiles(ServerConstants.PluginsPath, "*.obby", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    if (await this.TryOpenAsync(file) is PluginArchive archive)
                        archives.Add(archive);
                }

                await this.LoadArchivesAsync(archives);
                this.FinishStartup(ServerConstants.DataPacksPath);
            });

            DirectoryWatcher.Watch(ServerConstants.PluginsPath);
        }
        finally
        {
            this.startup?.Complete();
        }
    }

    public Task UnloadPluginsAsync() => this.RunChangeAsync(this.UnloadAllAsync);

    /// <summary>
    /// Loads a plugin file, or reloads it if it is loaded already.
    /// </summary>
    /// <param name="path">The plugin file.</param>
    /// <returns>The loaded plugin, or null if it couldn't load (or has to wait for its dependencies).</returns>
    /// <exception cref="InvalidOperationException">
    /// Called from a plugin's load, ready or unloading callback.
    /// </exception>
    public async Task<PluginContainer?> LoadPluginAsync(string path)
    {
        PluginContainer? loaded = null;

        await this.RunChangeAsync(async () =>
        {
            // Read inside the change, so that a deletion or a newer write handled meanwhile isn't undone.
            var archive = await this.TryOpenAsync(path);
            if (archive is null)
                return;

            var reloading = new List<PluginArchive> { archive };

            var old = this.plugins.FirstOrDefault(plugin => plugin.Source == path);
            if (old is not null)
            {
                // The dependents come back with it, from the archives they were loaded from.
                foreach (var unloaded in await this.UnloadWithDependentsAsync(old))
                {
                    if (unloaded != old)
                        reloading.Add(unloaded.Archive);
                }
            }

            this.stagedPlugins = this.stagedPlugins.RemoveAll(staged => staged.Source == path);

            await this.LoadArchivesAsync(reloading);
            this.OnPluginsChanged();

            loaded = this.plugins.FirstOrDefault(plugin => plugin.Source == path);
        });

        return loaded;
    }

    /// <summary>
    /// Unloads a plugin, and the plugins that depend on it, which wait for it to come back.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Called from a plugin's load, ready or unloading callback.
    /// </exception>
    public Task UnloadPluginAsync(PluginContainer pluginContainer) =>
        this.RunChangeAsync(() => this.UnloadAndRetryAsync(pluginContainer));

    public async ValueTask OnServerReadyAsync()
    {
        await this.RunChangeAsync(async () =>
        {
            this.serverReady = true;

            foreach (var pluginContainer in this.plugins)
                await this.StartAsync(pluginContainer);

            this.ResumeGenerators();
        });

        //THis only needs to be called once 😭😭
        CommandsRegistry.Register((Server)server);
    }

    /// <summary>
    /// Gets the PluginContainer either by specified assembly or by current executing assembly.
    /// </summary>
    /// <param name="assembly">The assembly you want to use to find the plugin container.</param>
    public PluginContainer GetPluginContainerByAssembly(Assembly? assembly = null) =>
        this.plugins.First(x => x.PluginAssembly == (assembly ?? Assembly.GetCallingAssembly()));

    public async ValueTask DisposeAsync()
    {
        // The watcher goes first, so that no file change starts a load once the plugins are gone.
        this.DirectoryWatcher.Dispose();

        await this.RunChangeAsync(async () =>
        {
            await this.UnloadAllAsync();
            this.disposed = true;
        });

        // Released plugins finish their unloading callbacks before the server stops, unless their calls hang.
        var pending = Task.WhenAll(this.releases.Values);
        if (await Task.WhenAny(pending, Task.Delay(drainTimeout)) != pending)
            Log.ReleasesTimedOut(this.logger, this.releases.Count);
    }

    /// <summary>
    /// Runs a change to the loaded plugins once the changes before it finished, unless the manager was disposed.
    /// </summary>
    private async Task RunChangeAsync(Func<Task> change)
    {
        if (changing.Value == this || creatingDimensions.Value is { Active: true })
        {
            throw new InvalidOperationException(
                "Plugins can't be loaded or unloaded from a plugin's load, ready or unloading callback, or while its "
                    + "dimensions are created.");
        }

        await this.changes.WaitAsync();
        try
        {
            if (this.disposed)
                return;

            changing.Value = this;
            await change();
        }
        finally
        {
            changing.Value = null;
            this.changes.Release();
        }
    }

    private Task UnloadPluginAsync(string path) => this.RunChangeAsync(async () =>
    {
        var staged = this.stagedPlugins.Length;
        this.stagedPlugins = this.stagedPlugins.RemoveAll(archive => archive.Source == path);

        if (this.plugins.FirstOrDefault(plugin => plugin.Source == path) is PluginContainer plugin)
        {
            await this.UnloadAndRetryAsync(plugin);
        }
        else if (this.stagedPlugins.Length != staged)
        {
            // A staged plugin can block others too, e.g. as one of a cycle.
            await this.LoadArchivesAsync([]);
            this.OnPluginsChanged();
        }
    });

    private Task RenameAsync(string oldPath, string newPath) => this.RunChangeAsync(() =>
    {
        foreach (var archive in this.plugins.Select(plugin => plugin.Archive).Concat(this.stagedPlugins))
        {
            if (archive.Source == oldPath)
                archive.Source = newPath;
        }

        return Task.CompletedTask;
    });

    // The dependents can't stay loaded without the plugin, so they wait for it to come back. Removing a plugin can also
    // let a staged one load, e.g. one that is incompatible with it.
    private async Task UnloadAndRetryAsync(PluginContainer pluginContainer)
    {
        if (!this.plugins.Contains(pluginContainer))
            return;

        foreach (var unloaded in await this.UnloadWithDependentsAsync(pluginContainer))
        {
            if (unloaded != pluginContainer)
                this.stagedPlugins = this.stagedPlugins.Add(unloaded.Archive);
        }

        await this.LoadArchivesAsync([]);
        this.OnPluginsChanged();
    }

    private async Task UnloadAllAsync()
    {
        foreach (var plugin in this.plugins.Reverse())
            await this.UnloadAsync(plugin);
    }

    /// <summary>
    /// Loads plugin archives together with the staged plugins, in dependency order. Plugins that can't load are staged.
    /// </summary>
    private async Task LoadArchivesAsync(IEnumerable<PluginArchive> archives)
    {
        var incoming = archives.ToList();

        // A staged copy of a plugin that is loaded, or that is loading now, is out of date.
        var staged = this.stagedPlugins.Where(archive => !this.plugins.Any(plugin => plugin.Info.Id == archive.Info.Id)
            && !incoming.Any(other => other.Info.Id == archive.Info.Id));

        var candidates = new List<PluginArchive>();
        foreach (var archive in incoming.Concat(staged))
        {
            if (archive.Info.Side is not PluginSide.Both && archive.Info.Side != this.Side)
            {
                Log.WrongSide(this.logger, archive.Info.Name, archive.Info.Side);
                continue;
            }

            candidates.Add(archive);
        }

        this.stagedPlugins = [];

        var byInfo = candidates.ToDictionary(archive => archive.Info);
        var resolution = PluginDependencyResolver.Resolve(
            [.. byInfo.Keys],
            [.. this.plugins.Select(plugin => plugin.Info)],
            this.Side);

        foreach (var warning in resolution.Warnings)
            Log.DependencyWarning(this.logger, warning);

        foreach (var (info, reason) in resolution.Rejected)
        {
            Log.Staged(this.logger, info.Name, reason);
            this.stagedPlugins = this.stagedPlugins.Add(byInfo[info]);
        }

        foreach (var info in resolution.LoadOrder)
        {
            var archive = byInfo[info];

            // A dependency that failed to load leaves its dependents waiting for it.
            var loaded = this.plugins.Select(plugin => plugin.Info);
            if (PluginDependencyResolver.FindMissingDependency(info, loaded, this.Side) is string missing)
            {
                Log.Staged(this.logger, info.Name, $"its dependency '{missing}' failed to load");
                this.stagedPlugins = this.stagedPlugins.Add(archive);
                continue;
            }

            await this.LoadAsync(archive);
        }
    }

    private async Task LoadAsync(PluginArchive archive)
    {
        var info = archive.Info;
        var stopwatch = Stopwatch.StartNew();

        var container = PluginContainer.Create(archive, this.plugins, this.Side);
        var dependencies = container.Dependencies;

        try
        {
            PluginAssemblyLoader.Load(container);

            // The plugin can use the server's services while it registers its own, with its own instances of those
            // that add things on its behalf.
            container.HostServices = new PluginHostServices(container, this.server, this.serverProvider);

            var serverOnly = new PluginServiceProvider(null, container.HostServices);
            PluginServiceHandler.InjectServices(serverOnly, container.Plugin!, this.logger);

            var services = this.CreatePluginServices(container.HostServices, dependencies);
            var baseServices = services.Count;

            container.Plugin!.ConfigureServices(services);
            container.SetServices(services, baseServices, this.serverProvider);

            // In the list before it registers, since code it calls may look it up (its load context's owner, say).
            this.plugins = this.plugins.Add(container);

            lock (container.WorldgenTypes)
                container.RegisteringWorldgenTypes = true;

            try
            {
                container.Plugin.ConfigureRegistry(
                    new PluginRegistry(this, this.eventDispatcher, this.commandHandler, container));
            }
            finally
            {
                lock (container.WorldgenTypes)
                    container.RegisteringWorldgenTypes = false;
            }

            var files = DataFiles.Of(archive);
            container.RecipeFiles = PluginRecipeFiles.Read(files, this.logger);
            container.TagFiles.InsertRange(0, this.ReadTagFiles(files));
            container.LootTableFiles = PluginLootTables.Read(files, this.logger);
            container.AdvancementFiles = AdvancementsRegistry.Read(files, this.logger);
            this.ApplyLootTables();
            this.ApplyAdvancements();
            container.FeatureFiles = this.ReadFeatureFiles(files);
            container.CodecFiles = this.ReadCodecFiles(files, container.FeatureFiles);

            // Read before tags, which may name the plugin's structures; they're applied with the features.
            (container.StructureFiles, container.StructureTemplates) = this.ReadStructureFiles(files);

            // Biome tags name the plugin's biomes, so they're applied once the biomes have ids.
            this.ApplyCodecs();
            this.ApplyTags();
            this.ApplyRecipeFiles();

            // Noise settings name blocks, and the plugin's types, which are registered by now.
            container.NoiseFiles = this.ReadNoiseFiles(files);
            this.ApplyNoise();

            // Level stems name biomes, dimension types and noise settings, the plugin's own included, so they're read
            // once those are registered, and their dimensions take part in ordering the features.
            container.DimensionFiles = this.ReadDimensionFiles(files, container);
            this.ApplyDimensions();
            this.ResumeGenerators();

            // Biome regions name biomes, and add to dimensions' biomes, whose features are ordered together.
            container.BiomeRegionFiles = this.ReadBiomeRegionFiles(archive, container);
            this.ApplyBiomeRegions();

            // Structures name features and processor lists, so they're applied with them.
            this.ApplyFeatures();
            this.ApplySpawns();
            await this.AddDimensionLevelsAsync();
            this.RespawnKeptEntities();
            container.Loaded = true;

            await container.Plugin.OnLoadedAsync(container.HostServices.Server);

            // Placed blocks of its types take part in redstone again, with their behaviour.
            this.RefreshPlacedBlocks([.. BlocksRegistry.PluginBlocks.Where(type => type.Owner == container)]);

            if (this.serverReady)
                await this.StartAsync(container);

            // Generators registered with the host's ILevelFactory from those callbacks count too.
            this.ResumeGenerators();
        }
        catch (Exception ex)
        {
            Log.LoadFailed(this.logger, ex, archive.Source);

            await this.UnloadAsync(container);
            return;
        }

        Log.Loaded(this.logger, info.Name, info.Version, stopwatch.ElapsedMilliseconds);
    }

    private async Task StartAsync(PluginContainer container)
    {
        container.InjectServices(this.logger);

        await container.Plugin!.OnServerReadyAsync(container.HostServices.Server);
    }

    /// <summary>
    /// Unloads a plugin and every loaded plugin that depends on it, dependents first.
    /// </summary>
    /// <returns>The unloaded plugins, in the order they had loaded.</returns>
    private async Task<List<PluginContainer>> UnloadWithDependentsAsync(PluginContainer root)
    {
        var affected = new HashSet<PluginContainer> { root };

        // Plugins are in load order, so a dependent always comes after what it depends on.
        foreach (var plugin in this.plugins)
        {
            if (plugin.Dependencies.Any(affected.Contains))
                affected.Add(plugin);
        }

        var unloading = this.plugins.Where(affected.Contains).ToList();

        for (var i = unloading.Count - 1; i >= 0; i--)
            await this.UnloadAsync(unloading[i]);

        return unloading;
    }

    /// <summary>
    /// Unregisters everything a plugin registered and removes it, then releases it once its running calls end.
    /// </summary>
    /// <remarks>
    /// A plugin is released after the plugins that depend on it, which may still use its services. Unloading waits for
    /// the release for a while, but not when it runs inside a call into a plugin (a plugin's command reloading it, say),
    /// which the release may be waiting for.
    /// </remarks>
    private async Task UnloadAsync(PluginContainer pluginContainer)
    {
        // Closed first, so that whatever the plugin registers meanwhile is refused rather than kept after unloading.
        pluginContainer.Close();

        foreach (var level in this.Levels())
        {
            level.ReleaseChunkTickets(pluginContainer);
            level.ReleaseLifecycle(pluginContainer);
        }

        // Movement abilities aren't saved, so only online players hold the plugin's grants.
        foreach (var player in this.server.OnlinePlayers.Values)
            player.ReleaseMovementAbilities(pluginContainer);

        // Before its handlers go, so that the plugin sees its menus close (to keep their contents, say). Its handlers
        // of that still run, although it's closed.
        await CloseMenusAsync(this.server, pluginContainer);

        this.commandHandler.UnregisterPluginCommands(pluginContainer);
        this.eventDispatcher.UnregisterPluginEvents(pluginContainer);

        if (this.server is Server concreteServer)
            concreteServer.PayloadChannels.UnregisterPluginPayloads(pluginContainer);

        // What the plugin added through the server's services, like boss bars, dialogs, recipes and scoreboards. Its
        // boss bars and scoreboards are retired, each in one step: hidden, and refusing changes from handlers still
        // running, which would show them again.
        pluginContainer.BossBars.Retire();

        if (pluginContainer.LoadContext is not null)
            DialogRegistry.Release(pluginContainer.LoadContext);

        foreach (var failure in pluginContainer.UndoHostAdditions())
            Log.UndoFailed(this.logger, failure, pluginContainer.Info.Name);

        ItemsRegistry.ReleasePlugin(pluginContainer);
        SoundEventsRegistry.ReleasePlugin(pluginContainer);
        CustomStatisticsRegistry.ReleasePlugin(pluginContainer);
        AdvancementTriggersRegistry.ReleasePlugin(pluginContainer);
        PortalTypesRegistry.ReleasePlugin(pluginContainer);
        DataComponentTypesRegistry.ReleasePlugin(pluginContainer);
        var blocks = BlocksRegistry.ReleasePlugin(pluginContainer);
        this.RefreshPlacedBlocks(blocks);

        PluginPotionMixes.ReleasePlugin(pluginContainer);
        if (this.server is Server recipeServer)
            recipeServer.RecipeRegistry.Types.ReleasePlugin(pluginContainer);
        EntityDefinitionsRegistry.ReleasePlugin(pluginContainer);
        BlockEntityTypesRegistry.ReleasePlugin(pluginContainer);
        PluginGenerators.Release(pluginContainer);

        // Once its handlers and its types' behaviours are gone, so that nothing of the plugin's runs as they go.
        await this.RemoveEntitiesAsync(pluginContainer);

        if (pluginContainer.LoadContext is not null)
            this.commandHandler.UnregisterArgumentParsers(pluginContainer.LoadContext);

        this.plugins = this.plugins.Remove(pluginContainer);
        this.ApplyLootTables();
        this.ApplyAdvancements();
        this.ApplyCodecs();
        this.ApplyTags();
        this.ApplyRecipeFiles();
        this.ApplyNoise();
        this.ApplyDimensions();
        this.ApplyBiomeRegions();
        this.ApplyFeatures();
        this.ApplySpawns();
        this.StopGenerators(pluginContainer);
        this.ResumeGenerators();

        var dependents = this.releases
            .Where(pair => pair.Key.Dependencies.Contains(pluginContainer))
            .Select(pair => pair.Value)
            .ToArray();

        var release = this.ReleaseWhenIdleAsync(pluginContainer, dependents);
        if (!release.IsCompleted)
        {
            this.releases[pluginContainer] = release;
            _ = release.ContinueWith(_ => this.releases.TryRemove(pluginContainer, out _), TaskScheduler.Default);
        }

        if (PluginCall.InsideAny)
            return;

        if (await Task.WhenAny(release, Task.Delay(drainTimeout)) != release)
            Log.DrainTimedOut(this.logger, pluginContainer.Info.Name, drainTimeout.TotalSeconds);
    }

    /// <summary>
    /// Closes the menus of <paramref name="pluginContainer"/>'s menu types that players have open, so that none stays
    /// interactive without the plugin, and disowns the types.
    /// </summary>
    /// <remarks>
    /// The types stop opening first: the plugin's calls still running as it unloads may end after the sweep, and could
    /// otherwise open its menus again. The types open again once a plugin registers them again.
    /// </remarks>
    internal static async Task CloseMenusAsync(IServer server, PluginContainer pluginContainer)
    {
        var owned = new List<MenuDefinition>();

        foreach (var menu in MenuTypesRegistry.PluginMenus)
        {
            lock (menu.Gate)
            {
                if (!ReferenceEquals(menu.Owner, pluginContainer))
                    continue;

                menu.IsOpenable = false;
                owned.Add(menu);
            }
        }

        foreach (var player in server.OnlinePlayers.Values)
        {
            if (player.OpenedContainer is MenuContainer open && owned.Contains(open.Menu))
                await player.CloseContainerAsync(pluginContainer);
        }

        foreach (var menu in owned)
        {
            lock (menu.Gate)
            {
                if (ReferenceEquals(menu.Owner, pluginContainer))
                    menu.Owner = null;
            }
        }
    }

    /// <summary>
    /// Removes the server players <paramref name="plugin"/> added, and the entities and block entities it owns from
    /// every level (see <see cref="PluginEntities"/> and <see cref="PluginBlockEntities"/>), as it unloads.
    /// </summary>
    /// <remarks>
    /// Each level's entities are removed between its ticks. Inside a call into a plugin, which a tick may be waiting
    /// for, or when a tick doesn't end in time, they're removed on the level's next tick instead, holding the plugin
    /// until then so that it isn't released before its block entities' last saves. The windows showing its block
    /// entities' containers close once they're removed.
    /// </remarks>
    private async Task RemoveEntitiesAsync(PluginContainer plugin)
    {
        if (this.server is Server concrete)
        {
            var players = concrete.OnlinePlayers.Values
                .OfType<ServerPlayer>()
                .Where(player => player.SpawnedBy == plugin)
                .ToList();

            // They leave like other players; the plugin's handlers of that are gone already.
            foreach (var player in players)
            {
                try
                {
                    await concrete.RemoveServerPlayerAsync(player);
                }
                catch (Exception ex)
                {
                    Log.EntityRemovalFailed(this.logger, ex, plugin.Info.Name);
                }
            }
        }

        foreach (var level in this.Levels())
        {
            // The server stopping disposes its worlds before it unloads the plugins: a disposed level saved their
            // block entities already and has no tick left to remove them on, so there's nothing to wait for.
            if (level.IsDisposed)
                continue;

            List<BaseContainer> retired = [];

            void Remove()
            {
                PluginEntities.RemoveFrom(level, plugin);
                retired = PluginBlockEntities.RemoveFrom(level, plugin);
            }

            if (!PluginCall.InsideAny && await level.TryRunBetweenTicksAsync(Remove, drainTimeout))
            {
                await PluginBlockEntities.CloseWindowsAsync(this.server, plugin, retired);
                continue;
            }

            // The removal saves the plugin's block entities a last time, in a call into it: the plugin isn't released
            // before that. Nothing of the plugin's starts meanwhile, so a level without any needs no hold.
            var hold = PluginBlockEntities.AnyIn(level, plugin) ? plugin.HoldUnloading() : null;
            level.EnqueueEntityAction(async () =>
            {
                try
                {
                    Remove();
                    await PluginBlockEntities.CloseWindowsAsync(this.server, plugin, retired);
                }
                finally
                {
                    hold?.Dispose();
                }
            });
        }

        // Its own container classes the players have open elsewhere, like a menu it opened for them.
        await PluginBlockEntities.CloseWindowsAsync(this.server, plugin, []);
    }

    // The placed blocks of plugin block types whose behaviour went or came back: the blocks around hear of them, as
    // the power, connections and analog output they give may have changed (see LevelBlockUpdates.RefreshBlocksOf).
    private void RefreshPlacedBlocks(List<API.Blocks.BlockType> types)
    {
        if (types.Count == 0)
            return;

        foreach (var level in this.Levels())
            level.BlockUpdates.RefreshBlocksOf(types);
    }

    // Spawns again the entities of plugins' types kept in loaded chunks while their plugins were unloaded, once their
    // types are loaded again.
    private void RespawnKeptEntities()
    {
        foreach (var level in this.Levels())
        {
            foreach (var region in level.Regions.Values.OfType<WorldData.Region>())
                region.RespawnKeptEntities(level);
        }
    }

    private async Task ReleaseWhenIdleAsync(PluginContainer pluginContainer, Task[] dependents)
    {
        await Task.WhenAll(dependents);
        await pluginContainer.WaitForCallsAsync();
        await this.ReleaseAsync(pluginContainer);
    }

    /// <summary>
    /// Runs a plugin's unloading callback, disposes it and its services, and unloads its load context, even if any of
    /// those fail.
    /// </summary>
    private async Task ReleaseAsync(PluginContainer pluginContainer)
    {
        var name = pluginContainer.Info.Name;
        var stopwatch = Stopwatch.StartNew();

        if (pluginContainer.Plugin is PluginBase plugin)
        {
            if (pluginContainer.Loaded)
            {
                try
                {
                    await plugin.OnUnloadingAsync();
                }
                catch (Exception ex)
                {
                    Log.UnloadingFailed(this.logger, ex, name);
                }
            }

            try
            {
                await plugin.DisposeAsync();
            }
            catch (Exception ex)
            {
                Log.DisposeFailed(this.logger, ex, name);
            }
        }

        var loadContext = pluginContainer.LoadContext;

        // Disposing drops the container's references into the load context, which can't unload while they exist.
        try
        {
            await pluginContainer.DisposeAsync();
        }
        catch (Exception ex)
        {
            Log.DisposeFailed(this.logger, ex, name);
        }

        // None of the plugin's code runs any more, its services' disposal included, so what it gave mobs can go.
        if (loadContext is not null)
            this.ReleaseFromMobs(loadContext);

        stopwatch.Stop();

        loadContext.Unloading += _ => Log.Unloaded(this.logger, name, stopwatch.ElapsedMilliseconds);
        loadContext.Unload();
    }

    // A plugin's or data pack's tag files, from its data folder. A malformed file is left out, like vanilla leaves out
    // a data pack's.
    private List<TagFile> ReadTagFiles(DataFiles source)
    {
        var files = new List<TagFile>();

        foreach (var (path, registry, identifier) in PluginTags.FindFiles(source.Paths))
        {
            try
            {
                var data = source.Read(path) ?? throw new InvalidDataException($"{path} can't be read.");
                files.Add(PluginTags.Parse(registry, identifier, $"{source.Name}/{path}", data));
            }
            catch (InvalidDataException ex)
            {
                Log.TagFileFailed(this.logger, ex, path, source.Location);
            }
        }

        return files;
    }

    // A plugin's or data pack's biome, dimension type and damage type files. A file that can't be read is left out;
    // the plugin or pack loads without it. Biomes may use vanilla's placed features and configured carvers, the
    // loaded plugins' and those of the features given, its own.
    private ImmutableArray<CodecFile> ReadCodecFiles(DataFiles source, ImmutableArray<FeatureFile> features)
    {
        var files = ImmutableArray.CreateBuilder<CodecFile>();
        var entries = features
            .Select(file => (file.Registry, file.Identifier))
            .ToHashSet();

        bool IsPlacedFeature(string identifier) =>
            entries.Contains((PluginFeatures.PlacedFeature, identifier)) || PluginFeatures.IsPlacedFeature(identifier);

        bool IsConfiguredCarver(string identifier) =>
            entries.Contains((PluginFeatures.ConfiguredCarver, identifier))
            || PluginFeatures.IsConfiguredCarver(identifier);

        foreach (var (path, registry, identifier) in PluginCodecs.FindFiles(source.Paths))
        {
            try
            {
                var data = source.Read(path) ?? throw new InvalidDataException($"{path} can't be read.");
                var name = $"{source.Name}/{path}";
                files.Add(PluginCodecs.Parse(registry, identifier, name, data, IsPlacedFeature, IsConfiguredCarver));
            }
            catch (InvalidDataException ex)
            {
                Log.CodecFileFailed(this.logger, ex, path, source.Location);
            }
        }

        // The dimension types and biomes level stems define inline are entries too (see LevelStemInline).
        foreach (var (path, stem) in PluginDimensions.FindFiles(source.Paths))
        {
            try
            {
                var data = source.Read(path) ?? throw new InvalidDataException($"{path} can't be read.");
                var name = $"{source.Name}/{path}";

                foreach (var entry in Registries.Codecs.LevelStemInline.Extract(stem, data).Entries)
                {
                    files.Add(PluginCodecs.Parse(
                        entry.Registry,
                        entry.Identifier,
                        name,
                        entry.Json,
                        IsPlacedFeature,
                        IsConfiguredCarver));
                }
            }
            catch (InvalidDataException ex)
            {
                Log.CodecFileFailed(this.logger, ex, path, source.Location);
            }
        }

        return files.ToImmutable();
    }

    // A plugin's or data pack's level stem files, given the container or pack owning them. A file that can't be read
    // is left out; the plugin or pack loads without it. During startup, one whose bytes were read is kept to read
    // again when every plugin and pack is read (see ReadStartupStems).
    private ImmutableArray<DimensionFile> ReadDimensionFiles(DataFiles source, object owner)
    {
        var files = ImmutableArray.CreateBuilder<DimensionFile>();

        foreach (var (path, identifier) in PluginDimensions.FindFiles(source.Paths))
        {
            var name = $"{source.Name}/{path}";
            byte[]? data = null;

            try
            {
                data = source.Read(path) ?? throw new InvalidDataException($"{path} can't be read.");
                files.Add(PluginDimensions.Parse(identifier, name, data));
            }
            catch (InvalidDataException ex)
            {
                if (data is not null && !this.dimensionTypesSettled)
                    this.startupStems.Add(new StartupStem(owner, path, identifier, name, source.Location, data));
                else
                    Log.CodecFileFailed(this.logger, ex, path, source.Location);
            }
        }

        return files.ToImmutable();
    }

    // Reads the stems startup kept once more, against the final plugins and data packs: those that read join their
    // plugin's or pack's files. Until dimension types settle, the others are kept to read again then; once they have,
    // they're left out, logged.
    // Returns whether a stem joined.
    private bool ReadStartupStems()
    {
        var joined = false;
        var unread = new List<StartupStem>();

        foreach (var stem in this.startupStems)
        {
            // A plugin that failed to load, or unloaded with a dependency, holds no files any more.
            if (stem.Owner is PluginContainer plugin && !this.plugins.Contains(plugin))
                continue;

            try
            {
                var file = PluginDimensions.Parse(stem.Identifier, stem.Name, stem.Bytes);

                switch (stem.Owner)
                {
                    case PluginContainer container:
                        container.DimensionFiles = container.DimensionFiles.Add(file);
                        break;
                    case DataPack pack:
                        pack.DimensionFiles = pack.DimensionFiles.Add(file);
                        break;
                }

                joined = true;
            }
            catch (InvalidDataException ex)
            {
                if (this.dimensionTypesSettled)
                    Log.CodecFileFailed(this.logger, ex, stem.Path, stem.Location);
                else
                    unread.Add(stem);
            }
        }

        this.startupStems.Clear();
        this.startupStems.AddRange(unread);

        return joined;
    }

    // A plugin's biome region files, then the regions it registered in code. A region that can't be read is left
    // out; the plugin loads without it.
    private ImmutableArray<BiomeRegionFile> ReadBiomeRegionFiles(PluginArchive archive, PluginContainer container)
    {
        var files = ImmutableArray.CreateBuilder<BiomeRegionFile>();

        foreach (var (path, identifier) in PluginBiomeRegions.FindFiles(archive.Paths))
        {
            try
            {
                var data = archive.Read(path) ?? throw new InvalidDataException($"{path} can't be read.");
                files.Add(PluginBiomeRegions.Parse(identifier, $"{archive.Info.Id}/{path}", data));
            }
            catch (InvalidDataException ex)
            {
                Log.CodecFileFailed(this.logger, ex, path, archive.Source);
            }
        }

        foreach (var (identifier, json) in container.RegisteredBiomeRegions)
        {
            try
            {
                files.Add(PluginBiomeRegions.Parse(identifier, $"{archive.Info.Id}'s registered {identifier}", json));
            }
            catch (InvalidDataException ex)
            {
                Log.CodecFileFailed(this.logger, ex, identifier, archive.Source);
            }
        }

        return files.ToImmutable();
    }

    // A plugin's or data pack's noise settings, density function, noise and parameter list files. A file that can't
    // be read is left out; the plugin or pack loads without it.
    private ImmutableArray<NoiseFile> ReadNoiseFiles(DataFiles source)
    {
        var files = ImmutableArray.CreateBuilder<NoiseFile>();

        foreach (var (path, registry, identifier) in PluginNoise.FindFiles(source.Paths))
        {
            try
            {
                var data = source.Read(path) ?? throw new InvalidDataException($"{path} can't be read.");
                files.Add(PluginNoise.Parse(registry, identifier, $"{source.Name}/{path}", data));
            }
            catch (InvalidDataException ex)
            {
                Log.CodecFileFailed(this.logger, ex, path, source.Location);
            }
        }

        return files.ToImmutable();
    }

    // A plugin's or data pack's configured feature, placed feature, configured carver and biome modifier files. A
    // file that can't be read is left out; the plugin or pack loads without it.
    private ImmutableArray<FeatureFile> ReadFeatureFiles(DataFiles source)
    {
        var files = ImmutableArray.CreateBuilder<FeatureFile>();

        foreach (var (path, registry, identifier) in PluginFeatures.FindFiles(source.Paths))
        {
            try
            {
                var data = source.Read(path) ?? throw new InvalidDataException($"{path} can't be read.");
                files.Add(PluginFeatures.Parse(registry, identifier, $"{source.Name}/{path}", data));
            }
            catch (InvalidDataException ex)
            {
                Log.CodecFileFailed(this.logger, ex, path, source.Location);
            }
        }

        return files.ToImmutable();
    }

    // A plugin's or data pack's structure, structure set and template pool files, and its structure templates, read
    // within its limits (see DataBudget): a template's NBT is gzipped, and could decompress to far more, so it counts
    // decompressed, together with a data pack's other files. A file that can't be read is left out; the plugin or
    // pack loads without it.
    private (ImmutableArray<StructureFile> Files, ImmutableArray<TemplateFile> Templates) ReadStructureFiles(
        DataFiles source)
    {
        var files = ImmutableArray.CreateBuilder<StructureFile>();

        foreach (var (path, registry, identifier) in PluginStructures.FindFiles(source.Paths))
        {
            try
            {
                var data = source.Read(path) ?? throw new InvalidDataException($"{path} can't be read.");
                files.Add(PluginStructures.Parse(registry, identifier, $"{source.Name}/{path}", data));
            }
            catch (InvalidDataException ex)
            {
                Log.CodecFileFailed(this.logger, ex, path, source.Location);
            }
        }

        var templates = ImmutableArray.CreateBuilder<TemplateFile>();
        var budget = source.Budget;

        foreach (var (path, identifier) in PluginStructures.FindTemplates(source.Paths).Take(budget.Limits.Files))
        {
            try
            {
                var data = source.Read(path) ?? throw new InvalidDataException($"{path} can't be read.");
                var (template, read) = PluginStructures.ParseTemplate(
                    identifier,
                    $"{source.Name}/{path}",
                    data,
                    budget.Available(path));

                budget.Charge(path, read);
                templates.Add(template);
            }
            catch (InvalidDataException ex)
            {
                Log.CodecFileFailed(this.logger, ex, path, source.Location);
            }
        }

        return (files.ToImmutable(), templates.ToImmutable());
    }

    /// <summary>
    /// Ends startup once the plugins in the plugins folder loaded: the operator's data packs in
    /// <paramref name="dataPacks"/> win over plugins' files, so they're applied after them, then dimension types, and
    /// the stems naming them, are settled.
    /// </summary>
    internal void FinishStartup(string dataPacks)
    {
        this.LoadDataPacks(dataPacks);
        this.SettleDimensionTypes();
    }

    // Reads the data packs folder's packs, in the order plugins' files are read, and applies them after the plugins'.
    // Data packs are read once: a change applies after a restart.
    private void LoadDataPacks(string folder)
    {
        var packs = DataPack.OpenAll(folder, ReadsDataFile, this.logger);
        if (packs.Count == 0)
            return;

        foreach (var pack in packs)
        {
            pack.TagFiles.AddRange(this.ReadTagFiles(pack.Files));
            pack.LootTableFiles = PluginLootTables.Read(pack.Files, this.logger);
            pack.AdvancementFiles = AdvancementsRegistry.Read(pack.Files, this.logger);
            pack.FeatureFiles = this.ReadFeatureFiles(pack.Files);
            (pack.StructureFiles, pack.StructureTemplates) = this.ReadStructureFiles(pack.Files);
        }

        // Biomes may name any pack's features.
        ImmutableArray<FeatureFile> features = [.. packs.SelectMany(pack => pack.FeatureFiles)];
        foreach (var pack in packs)
            pack.CodecFiles = this.ReadCodecFiles(pack.Files, features);

        this.dataPacks = [.. packs];
        this.ApplyLootTables();
        this.ApplyAdvancements();
        this.ApplyCodecs();
        this.ApplyTags();
        this.ApplyRecipeFiles();

        foreach (var pack in packs)
            pack.NoiseFiles = this.ReadNoiseFiles(pack.Files);

        this.ApplyNoise();

        foreach (var pack in packs)
        {
            pack.DimensionFiles = this.ReadDimensionFiles(pack.Files, pack);
        }

        this.ApplyDimensions();
        this.ApplyFeatures();
        this.ApplySpawns();

        foreach (var pack in packs)
        {
            var read = pack.TagFiles.Count
                + pack.LootTableFiles.Length
                + pack.AdvancementFiles.Length
                + pack.FeatureFiles.Length
                + pack.CodecFiles.Length
                + pack.NoiseFiles.Length
                + pack.DimensionFiles.Length
                + pack.StructureFiles.Length
                + pack.StructureTemplates.Length;

            var leftOut = pack.Files.Paths.Count(path => !ReadsDataFile(path));
            Log.DataPackLoaded(this.logger, pack.Files.Name, read, leftOut);

            // What was read is in the files by now.
            pack.DropContents();
        }
    }

    // Whether Obsidian reads a plugin's or data pack's data file, by its path; others, like recipes,
    // are left out.
    private static bool ReadsDataFile(string path)
    {
        string[] paths = [path];

        return PluginTags.FindFiles(paths).Any()
            || PluginLootTables.FindFiles(paths).Any()
            || AdvancementsRegistry.FindFiles(paths).Any()
            || PluginFeatures.FindFiles(paths).Any()
            || PluginCodecs.FindFiles(paths).Any()
            || PluginNoise.FindFiles(paths).Any()
            || PluginDimensions.FindFiles(paths).Any()
            || PluginStructures.FindFiles(paths).Any()
            || PluginStructures.FindTemplates(paths).Any();
    }

    private void ApplyAdvancements() => AdvancementsRegistry.Apply(
        this.DataFilesOf(plugin => plugin.AdvancementFiles, pack => pack.AdvancementFiles));

    private void ApplyLootTables() => PluginLootTables.Apply(
        this.DataFilesOf(plugin => plugin.LootTableFiles, pack => pack.LootTableFiles));

    // The files plugins hold in load order, then data packs' in theirs, which win.
    private IEnumerable<T> DataFilesOf<T>(
        Func<PluginContainer, IEnumerable<T>> plugin,
        Func<DataPack, IEnumerable<T>> pack) =>
        this.plugins.SelectMany(plugin).Concat(this.dataPacks.SelectMany(pack));

    // Has every loaded entity drop the goals, navigators and goal controllers plugins gave it from the load context, on
    // its level's tick, where its AI runs. The queued actions hold the context weakly: a level that doesn't tick would
    // keep it loaded otherwise, and once nothing else holds it, no mob has anything to drop.
    private void ReleaseFromMobs(PluginLoadContext loadContext)
    {
        var levels = this.Levels();

        var weakContext = new WeakReference<PluginLoadContext>(loadContext);

        foreach (var level in levels)
        {
            level.EnqueueEntityAction(() =>
            {
                if (!weakContext.TryGetTarget(out var context))
                    return default;

                var entities = level.Regions.Values
                    .OfType<WorldData.Region>()
                    .SelectMany(region => region.Entities.Values)
                    .OfType<Entities.Entity>();

                // One entity failing mustn't keep the others holding the plugin.
                foreach (var entity in entities)
                {
                    try
                    {
                        entity.ReleasePluginObjects(context);
                    }
                    catch (Exception ex)
                    {
                        Log.MobReleaseFailed(this.logger, ex, entity.EntityId);
                    }
                }

                return default;
            });
        }
    }

    // The tags hold vanilla's entries and the loaded plugins' and data packs' tag files, in order. Placed feature,
    // configured carver and structure tags may name their entries.
    private void ApplyTags() => PluginTags.Apply(
        this.DataFilesOf(plugin => plugin.TagFiles, pack => pack.TagFiles),
        this.logger,
        this.DataFilesOf(plugin => plugin.FeatureFiles, pack => pack.FeatureFiles)
            .Select(file => (file.Registry, file.Identifier))
            .Concat(this.DataFilesOf(plugin => plugin.StructureFiles, pack => pack.StructureFiles)
                .Select(file => (file.Registry, file.Identifier))));

    // The loaded plugins' and data packs' entries are in their registries, the last file of each winning. Plugins'
    // entries no file has any more stay; vanilla's are vanilla's again. Vanilla's dimension types keep their height
    // once startup settled them, and those kept vanilla's then stay so.
    private void ApplyCodecs() => PluginCodecs.Apply(
        this.DataFilesOf(plugin => plugin.CodecFiles, pack => pack.CodecFiles)
            .Where(file => file.Registry != PluginCodecs.DimensionType
                || !this.keptDimensionTypes.Contains(file.Identifier)),
        this.logger,
        levelsCreated: this.dimensionTypesSettled || this.Levels().Count > 0);

    // Once every startup plugin and data pack is read, and before the worlds create their levels, a vanilla dimension
    // type whose build range a vanilla dimension, as overridden, can't generate in stays vanilla's. Vanilla clamps
    // generation to the build range instead, but Obsidian's generator fills every cell of the noise settings; a level
    // stem's own pairing is checked as it's read (see LevelStemData). The stems startup kept are read first, against
    // every plugin's and pack's data, so that a vanilla dimension is checked with the noise settings its stem names
    // even when they came with a later pack. Stems are then read against the settled types, those startup kept
    // included, and only now is one that can't be read logged.
    private void SettleDimensionTypes()
    {
        var joined = this.ReadStartupStems();
        if (joined)
            this.ApplyDimensions();

        var misfits = PluginDimensions.VanillaTypeMisfits();

        foreach (var (type, reason) in misfits)
            Log.DimensionTypeKept(this.logger, type, reason);

        if (misfits.Count > 0)
        {
            this.keptDimensionTypes = [.. misfits.Keys];
            this.ApplyCodecs();
        }

        this.dimensionTypesSettled = true;

        joined |= this.ReadStartupStems();
        this.ApplyDimensions();

        if (misfits.Count > 0 || joined)
            this.ApplyFeatures();
    }

    // Dimensions read from now on use the loaded plugins' and data packs' noise settings, density functions, noises
    // and parameter lists.
    private void ApplyNoise() => PluginNoise.Apply(
        this.DataFilesOf(plugin => plugin.NoiseFiles, pack => pack.NoiseFiles),
        this.plugins.SelectMany(plugin => plugin.WorldgenTypes),
        this.logger);

    // Every world's levels, dimensions included.
    private List<WorldData.AbstractLevel> Levels() => [.. this.server.WorldManager.GetAvailableWorlds()
        .OfType<WorldData.World>()
        .SelectMany(world => world.dimensions.Values.OfType<WorldData.AbstractLevel>().Prepend(world))];

    // A level generating with an unloading plugin's generator, or a dimension whose generator is made of its types,
    // can't call it any more: the level stops generating new chunks (a chunk or step running now finishes first,
    // holding the plugin), and its generator lets go of the plugin's code so it can unload. Levels made of vanilla's
    // types and plugins' data keep generating.
    private void StopGenerators(PluginContainer plugin)
    {
        // Levels not registered yet included, whose plugin generator may have started while the plugin was closing.
        foreach (var level in WorldData.Generators.PluginLevelGenerator.StopAll(plugin))
            Log.GeneratorStopped(this.logger, level, plugin.Info.Name);

        foreach (var level in this.Levels())
        {
            if (level.Generator is WorldData.Generators.MojangGenerator generator
                && generator.Dimension.Plugins.Contains(plugin))
            {
                if (generator.Builder is not null)
                    Log.GeneratorStopped(this.logger, generator.Dimension.Identifier, plugin.Info.Name);

                generator.Stop();
            }
        }

        // The plugin is out of the list by now, but its files' dimensions may be made of its types too.
        PluginDimensions.Release(
            plugin,
            this.DataFilesOf(loaded => loaded.DimensionFiles, pack => pack.DimensionFiles)
                .Concat(plugin.DimensionFiles));
    }

    // Levels that stopped generating start again once they can, from their next chunk: a level whose plugin generator
    // a loaded plugin registers, and a dimension whose generator's parts are back (read again from its stem, see
    // PluginDimensions.Apply). One stopped by plugins' code failing waits until those plugins reloaded or unloaded,
    // and one whose level stem changed meanwhile waits for a restart.
    private void ResumeGenerators()
    {
        foreach (var level in this.Levels())
        {
            try
            {
                switch (level.Generator)
                {
                    case WorldData.Generators.PluginLevelGenerator generator
                        when generator.Plugin is null && PluginGenerators.Find(generator.Id) is PluginGenerator found:
                        generator.Start(found);
                        break;

                    case WorldData.Generators.MojangGenerator generator when generator.Builder is null:
                        this.Resume(generator);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.ResumeFailed(this.logger, ex, level.Name);
            }
        }
    }

    private void Resume(WorldData.Generators.MojangGenerator generator)
    {
        if (generator.FailedBy.Any(this.plugins.Contains))
            return;

        // A dimension built from a stem is read again once released; vanilla's compiled ones are never released.
        var current = generator.Dimension;
        var dimension = PluginDimensions.Find(current.Identifier) as WorldData.Generators.Mojang.MojangDimension
            ?? current;
        if (dimension.IsReleased)
            return;

        if (generator.Resume(dimension))
            Log.GeneratorResumed(this.logger, dimension.Identifier);
    }

    // Dimensions not generating yet take the loaded plugins' biome regions; generating ones keep theirs.
    private void ApplyBiomeRegions() =>
        PluginBiomeRegions.Apply(this.plugins.SelectMany(plugin => plugin.BiomeRegionFiles), this.logger);

    // The loaded plugins' and data packs' dimensions are added. Ones no plugin has any more stay, with their levels.
    // Vanilla's dimensions follow their overrides until a level takes them. During startup, a stem may not read only
    // until a later plugin or pack, or until dimension types settle, so what Apply logs waits until they have.
    private void ApplyDimensions() => PluginDimensions.Apply(
        this.DataFilesOf(plugin => plugin.DimensionFiles, pack => pack.DimensionFiles),
        this.dimensionTypesSettled ? this.logger : NullLogger.Instance);

    // A plugin's new dimensions get their levels as it loads. Before the worlds load, loading them creates the levels
    // instead, and a plugin loading while they do waits for that (see WorldManager.AddPluginDimensionsAsync).
    private async Task AddDimensionLevelsAsync()
    {
        if (this.server.WorldManager is WorldData.WorldManager worldManager)
            await worldManager.AddPluginDimensionsAsync();
    }

    // World generation places the loaded plugins' features, with their biome modifiers applied. Every level's
    // generator moves to them now, so none keeps the previous features (and the plugins they came from). Features
    // that a generating dimension can't order aren't applied: the previous ones stay (see PluginFeatures.Apply).
    // A generator may stop (and drop its builder) at any time, so each builder is read once; refreshing one that
    // stopped meanwhile is harmless.
    private void ApplyFeatures()
    {
        var builders = this.Levels()
            .Select(level => level.Generator)
            .OfType<WorldData.Generators.MojangGenerator>()
            .Select(generator => (generator.Dimension.Identifier, generator.Builder))
            .Where(entry => entry.Builder is not null)
            .ToList();

        var applied = PluginFeatures.Apply(
            this.DataFilesOf(plugin => plugin.FeatureFiles, pack => pack.FeatureFiles),
            this.DataFilesOf(plugin => plugin.CodecFiles, pack => pack.CodecFiles),
            this.plugins.SelectMany(plugin => plugin.WorldgenTypes),
            builders.Select(entry => entry.Identifier).ToHashSet(),
            this.logger);

        if (applied)
        {
            foreach (var (dimension, builder) in builders)
            {
                if (!builder!.RefreshFeatures())
                    Log.KeptPreviousFeatures(this.logger, dimension);
            }
        }

        this.ApplyStructures();
    }

    // World generation starts and places the loaded plugins' and data packs' structures, which name the current
    // features and processor lists. Every level's generator moves to them now, and drops the starts of structure sets
    // that are gone, so none keeps the plugins they came from; chunks not generated yet get the new sets' structures.
    private void ApplyStructures()
    {
        PluginStructures.Apply(
            this.DataFilesOf(plugin => plugin.StructureFiles, pack => pack.StructureFiles),
            this.DataFilesOf(plugin => plugin.StructureTemplates, pack => pack.StructureTemplates),
            PluginFeatures.Current,
            this.plugins.SelectMany(plugin => plugin.WorldgenTypes),
            this.logger);

        foreach (var level in this.Levels())
        {
            if (level.Generator is WorldData.Generators.MojangGenerator generator)
                generator.RefreshStructures();
        }
    }

    // Natural spawning uses the loaded plugins' biomes' spawners and spawn biome modifiers from now on.
    private void ApplySpawns() => PluginSpawns.Apply(
        this.DataFilesOf(plugin => plugin.FeatureFiles, pack => pack.FeatureFiles),
        this.DataFilesOf(plugin => plugin.CodecFiles, pack => pack.CodecFiles),
        this.logger);

    // Players see the commands of the plugins that are loaded now. Startup doesn't need this: nobody has joined yet.
    private void OnPluginsChanged()
    {
        if (!this.serverReady)
            return;

        CommandsRegistry.Register((Server)this.server);

        foreach (var player in this.server.OnlinePlayers.Values)
            player.SendPacket(CommandsRegistry.Packet);

        // Clients catch up with the plugins' content outside the plugin change, which reconfiguring players' leave
        // handlers mustn't wait on.
        _ = this.RefreshClientsAsync();
    }

    private async Task RefreshClientsAsync()
    {
        try
        {
            await ((Server)this.server).RefreshClientsAsync();
        }
        catch (Exception ex)
        {
            Log.RefreshClientsFailed(this.logger, ex);
        }
    }

    /// <summary>
    /// The services every plugin starts with, its own instances of the host's among them, plus the singletons of the
    /// plugins it depends on.
    /// </summary>
    private ServiceCollection CreatePluginServices(
        PluginHostServices hostServices,
        ImmutableArray<PluginContainer> dependencies)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConfiguration(this.configuration);
        });

        hostServices.AddTo(services);
        services.AddSingleton(this.serverProvider.GetRequiredService<IOptionsMonitor<ServerConfiguration>>());

        // Added as instances, which the dependent's provider doesn't dispose: they belong to the dependency.
        foreach (var dependency in dependencies)
        {
            foreach (var type in dependency.ExportedSingletons)
                services.AddSingleton(type, dependency.ServiceProvider.GetRequiredService(type));
        }

        return services;
    }

    private async Task<PluginArchive?> TryOpenAsync(string path)
    {
        try
        {
            return await PluginArchive.OpenAsync(path);
        }
        catch (Exception ex)
        {
            Log.LoadFailed(this.logger, ex, path);
            return null;
        }
    }

    // A level stem file startup couldn't read yet, with its bytes, as a data pack's contents are dropped once read.
    // Owner is the plugin container or data pack holding it; Name says where it comes from, Location where its
    // plugin or pack is.
    private sealed record StartupStem(
        object Owner,
        string Path,
        string Identifier,
        string Name,
        string Location,
        byte[] Bytes);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Updating connected players to the plugins' content failed")]
        public static partial void RefreshClientsFailed(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "{Dimension} keeps placing its previous features: it can't order the new ones")]
        public static partial void KeptPreviousFeatures(ILogger logger, string dimension);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Couldn't drop an unloaded plugin's AI from entity {EntityId}")]
        public static partial void MobReleaseFailed(ILogger logger, Exception exception, int entityId);

        [LoggerMessage(Level = LogLevel.Error, Message = "Left out tag file {Path} of {Source}")]
        public static partial void TagFileFailed(ILogger logger, Exception exception, string path, string source);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Left out data file {Path} of {Source}")]
        public static partial void CodecFileFailed(ILogger logger, Exception exception, string path, string source);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "Loaded data pack {Name}: read {Read} files, left out {LeftOut} that Obsidian doesn't read")]
        public static partial void DataPackLoaded(ILogger logger, string name, int read, int leftOut);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Kept vanilla's dimension type {Type}: {Reason}, which Obsidian can't clamp generation to yet")]
        public static partial void DimensionTypeKept(ILogger logger, string type, string reason);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "Loaded plugin {PluginName} {Version} in {ElapsedMilliseconds}ms")]
        public static partial void Loaded(ILogger logger, string pluginName, Version version, long elapsedMilliseconds);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load plugin {Path}")]
        public static partial void LoadFailed(ILogger logger, Exception exception, string path);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Plugin {PluginName} can't load yet: {Reason}")]
        public static partial void Staged(ILogger logger, string pluginName, string reason);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{Warning}")]
        public static partial void DependencyWarning(ILogger logger, string warning);

        [LoggerMessage(Level = LogLevel.Debug,
            Message = "Skipping plugin {PluginName}: it only runs on the {Side} side")]
        public static partial void WrongSide(ILogger logger, string pluginName, PluginSide side);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Plugin {PluginName} is released once its handlers finish; they still ran after {Seconds}s")]
        public static partial void DrainTimedOut(ILogger logger, string pluginName, double seconds);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Stopping with {Count} plugins not released: their handlers still run")]
        public static partial void ReleasesTimedOut(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "Unloaded plugin {PluginName} in {ElapsedMilliseconds}ms")]
        public static partial void Unloaded(ILogger logger, string pluginName, long elapsedMilliseconds);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Couldn't remove a server player of unloading plugin {PluginName}")]
        public static partial void EntityRemovalFailed(ILogger logger, Exception exception, string pluginName);

        [LoggerMessage(Level = LogLevel.Error, Message = "Plugin {PluginName} failed while unloading")]
        public static partial void UnloadingFailed(ILogger logger, Exception exception, string pluginName);

        [LoggerMessage(Level = LogLevel.Error, Message = "Disposing plugin {PluginName} failed")]
        public static partial void DisposeFailed(ILogger logger, Exception exception, string pluginName);

        [LoggerMessage(Level = LogLevel.Error, Message = "Removing what {PluginName} added to the server failed")]
        public static partial void UndoFailed(ILogger logger, Exception exception, string pluginName);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "{Level} stopped generating new chunks until {PluginName} loads again: it generates with its "
                + "code, which unloaded")]
        public static partial void GeneratorStopped(ILogger logger, string level, string pluginName);

        [LoggerMessage(Level = LogLevel.Information, Message = "Dimension {Dimension} generates new chunks again")]
        public static partial void GeneratorResumed(ILogger logger, string dimension);

        [LoggerMessage(Level = LogLevel.Error, Message = "{Level} couldn't start generating again")]
        public static partial void ResumeFailed(ILogger logger, Exception exception, string level);
    }
}

// thank you Roxxel && DorrianD3V for the invasion <3
// thank you Jonpro03 for your awesome contributions
// thank you Sebastian for your amazing plugin framework <3
// thank you Tides, Craftplacer for being part of the team early on <3
