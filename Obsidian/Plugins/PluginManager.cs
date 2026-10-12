using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using Obsidian.API.Plugins;
using Obsidian.Commands.Framework;
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
/// Everything a plugin registers (commands, events, payload channels, services) is removed when it unloads, so that its
/// collectible load context can unload too.
/// </para>
/// </remarks>
public sealed partial class PluginManager : IAsyncDisposable
{
    // How long unloading, and stopping, wait for plugins' running handlers to finish before going on without them.
    private static readonly TimeSpan drainTimeout = TimeSpan.FromSeconds(5);

    // Set while a change runs on this async flow, to catch a plugin changing plugins from its own lifecycle callback,
    // which would wait for itself.
    private static readonly AsyncLocal<PluginManager?> changing = new();

    internal readonly ILogger logger;
    private readonly IConfiguration configuration;
    internal readonly IServer server;

    private readonly IServiceProvider serverProvider;
    private readonly CommandHandler commandHandler;
    private readonly EventDispatcher eventDispatcher;
    private readonly IPluginRegistry pluginRegistry;
    private readonly PluginStartup? startup;

    // Loading and unloading change the plugin list and what is registered, so they happen one at a time.
    private readonly SemaphoreSlim changes = new(1, 1);

    // Unloaded plugins waiting for their running calls to end before they are released (see UnloadAsync).
    private readonly ConcurrentDictionary<PluginContainer, Task> releases = new();

    private ImmutableArray<PluginContainer> plugins = [];
    private ImmutableArray<PluginArchive> stagedPlugins = [];
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
        this.pluginRegistry = new PluginRegistry(this, eventDispatcher, commandHandler);

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
                var archives = new List<PluginArchive>();
                var files = Directory.GetFiles(ServerConstants.PluginsPath, "*.obby", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    if (await this.TryOpenAsync(file) is PluginArchive archive)
                        archives.Add(archive);
                }

                await this.LoadArchivesAsync(archives);
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
        if (changing.Value == this)
            throw new InvalidOperationException(
                "Plugins can't be loaded or unloaded from a plugin's load, ready or unloading callback.");

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
            var missing = info.Dependencies.FirstOrDefault(dependency => dependency.Required
                && PluginDependencyResolver.AppliesTo(dependency, this.Side)
                && !this.plugins.Any(plugin => plugin.Info.Id == dependency.Id));
            if (missing.Id is not null)
            {
                Log.Staged(this.logger, info.Name, $"its dependency '{missing.Id}' failed to load");
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

        var dependencies = this.plugins
            .Where(plugin => info.Dependencies.Any(dependency => dependency.Id == plugin.Info.Id
                && PluginDependencyResolver.AppliesTo(dependency, this.Side)))
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

        try
        {
            PluginAssemblyLoader.Load(container);

            // The plugin can use the server's services while it registers its own.
            var serverOnly = new PluginServiceProvider(null, this.serverProvider);
            PluginServiceHandler.InjectServices(serverOnly, container.Plugin!, this.logger);

            var services = this.CreatePluginServices(dependencies);
            var baseServices = services.Count;

            container.Plugin!.ConfigureServices(services);
            container.SetServices(services, baseServices, this.serverProvider);

            // Registering looks the plugin up by its assembly, so it has to be in the list first.
            this.plugins = this.plugins.Add(container);

            lock (container.WorldgenTypes)
                container.RegisteringWorldgenTypes = true;

            try
            {
                container.Plugin.ConfigureRegistry(this.pluginRegistry);
            }
            finally
            {
                lock (container.WorldgenTypes)
                    container.RegisteringWorldgenTypes = false;
            }

            container.TagFiles.InsertRange(0, this.ReadTagFiles(archive));
            container.FeatureFiles = this.ReadFeatureFiles(archive);
            container.CodecFiles = this.ReadCodecFiles(archive, container.FeatureFiles);
            this.ApplyTags();
            this.ApplyCodecs();

            // Level stems name biomes and dimension types, the plugin's own included, so they're read once those are
            // registered, and their dimensions take part in ordering the features.
            container.DimensionFiles = this.ReadDimensionFiles(archive);
            this.ApplyDimensions();
            this.ApplyFeatures();
            await this.AddDimensionLevelsAsync();
            container.Loaded = true;

            await container.Plugin.OnLoadedAsync(this.server);

            if (this.serverReady)
                await this.StartAsync(container);
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

        await container.Plugin!.OnServerReadyAsync(this.server);
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

        this.commandHandler.UnregisterPluginCommands(pluginContainer);
        this.eventDispatcher.UnregisterPluginEvents(pluginContainer);

        if (this.server is Server concreteServer)
            concreteServer.PayloadChannels.UnregisterPluginPayloads(pluginContainer);

        ItemsRegistry.ReleasePlugin(pluginContainer);
        BlocksRegistry.ReleasePlugin(pluginContainer);
        EntityDefinitionsRegistry.ReleasePlugin(pluginContainer);

        if (pluginContainer.LoadContext is not null)
            this.commandHandler.UnregisterArgumentParsers(pluginContainer.LoadContext);

        this.plugins = this.plugins.Remove(pluginContainer);
        this.ApplyTags();
        this.ApplyCodecs();
        this.ApplyFeatures();

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

    // A plugin's tag files, from its data folder like a data pack's. A malformed file is left out, like vanilla leaves
    // out a data pack's.
    private List<TagFile> ReadTagFiles(PluginArchive archive)
    {
        var files = new List<TagFile>();

        foreach (var (path, registry, identifier) in PluginTags.FindFiles(archive.Paths))
        {
            try
            {
                var data = archive.Read(path) ?? throw new InvalidDataException($"{path} can't be read.");
                files.Add(PluginTags.Parse(registry, identifier, $"{archive.Info.Id}/{path}", data));
            }
            catch (InvalidDataException ex)
            {
                Log.TagFileFailed(this.logger, ex, path, archive.Source);
            }
        }

        return files;
    }

    // A plugin's biome, dimension type and damage type files. A file that can't be read, or would override one of
    // vanilla's entries, is left out; the plugin loads without it. Biomes may use vanilla's placed features, the
    // loaded plugins' and the plugin's own.
    private ImmutableArray<CodecFile> ReadCodecFiles(PluginArchive archive, ImmutableArray<FeatureFile> features)
    {
        var files = ImmutableArray.CreateBuilder<CodecFile>();
        var placedFeatures = features
            .Where(file => file.Registry == PluginFeatures.PlacedFeature)
            .Select(file => file.Identifier)
            .ToHashSet();

        bool IsPlacedFeature(string identifier) =>
            placedFeatures.Contains(identifier) || PluginFeatures.IsPlacedFeature(identifier);

        foreach (var (path, registry, identifier) in PluginCodecs.FindFiles(archive.Paths))
        {
            try
            {
                var data = archive.Read(path) ?? throw new InvalidDataException($"{path} can't be read.");
                var source = $"{archive.Info.Id}/{path}";
                files.Add(PluginCodecs.Parse(registry, identifier, source, data, IsPlacedFeature));
            }
            catch (InvalidDataException ex)
            {
                Log.CodecFileFailed(this.logger, ex, path, archive.Source);
            }
        }

        return files.ToImmutable();
    }

    // A plugin's level stem files. A file that can't be read, or would override one of vanilla's dimensions, is left
    // out; the plugin loads without it.
    private ImmutableArray<DimensionFile> ReadDimensionFiles(PluginArchive archive)
    {
        var files = ImmutableArray.CreateBuilder<DimensionFile>();

        foreach (var (path, identifier) in PluginDimensions.FindFiles(archive.Paths))
        {
            try
            {
                var data = archive.Read(path) ?? throw new InvalidDataException($"{path} can't be read.");
                files.Add(PluginDimensions.Parse(identifier, $"{archive.Info.Id}/{path}", data));
            }
            catch (InvalidDataException ex)
            {
                Log.CodecFileFailed(this.logger, ex, path, archive.Source);
            }
        }

        return files.ToImmutable();
    }

    // A plugin's configured feature, placed feature and biome modifier files. A file that can't be read is left out;
    // the plugin loads without it.
    private ImmutableArray<FeatureFile> ReadFeatureFiles(PluginArchive archive)
    {
        var files = ImmutableArray.CreateBuilder<FeatureFile>();

        foreach (var (path, registry, identifier) in PluginFeatures.FindFiles(archive.Paths))
        {
            try
            {
                var data = archive.Read(path) ?? throw new InvalidDataException($"{path} can't be read.");
                files.Add(PluginFeatures.Parse(registry, identifier, $"{archive.Info.Id}/{path}", data));
            }
            catch (InvalidDataException ex)
            {
                Log.CodecFileFailed(this.logger, ex, path, archive.Source);
            }
        }

        return files.ToImmutable();
    }

    // Has every loaded entity drop the goals, navigators and goal controllers plugins gave it from the load context, on
    // its level's tick, where its AI runs. The queued actions hold the context weakly: a level that doesn't tick would
    // keep it loaded otherwise, and once nothing else holds it, no mob has anything to drop.
    private void ReleaseFromMobs(PluginLoadContext loadContext)
    {
        var levels = this.server.WorldManager.GetAvailableWorlds()
            .OfType<WorldData.World>()
            .SelectMany(world => world.dimensions.Values.OfType<WorldData.AbstractLevel>().Prepend(world));

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

    // The tags hold vanilla's entries and the loaded plugins' tag files, in load order.
    private void ApplyTags() =>
        PluginTags.Apply(this.plugins.SelectMany(plugin => plugin.TagFiles), this.logger);

    // The loaded plugins' entries are in their registries, in load order. Ones no plugin has any more stay.
    private void ApplyCodecs() =>
        PluginCodecs.Apply(this.plugins.SelectMany(plugin => plugin.CodecFiles), this.logger);

    // The loaded plugins' dimensions are added. Ones no plugin has any more stay, with their levels.
    private void ApplyDimensions() =>
        PluginDimensions.Apply(this.plugins.SelectMany(plugin => plugin.DimensionFiles), this.logger);

    // Once the worlds are loaded, a plugin's new dimensions get their levels as it loads; before, the worlds create
    // them as they load.
    private async Task AddDimensionLevelsAsync()
    {
        if (this.server.WorldManager is WorldData.WorldManager { ReadyToJoin: true } worldManager)
            await worldManager.AddPluginDimensionsAsync();
    }

    // World generation places the loaded plugins' features, with their biome modifiers applied. Every level's
    // generator moves to them now, so none keeps the previous features (and the plugins they came from). Features
    // that a generating dimension can't order aren't applied: the previous ones stay (see PluginFeatures.Apply).
    private void ApplyFeatures()
    {
        var generators = this.server.WorldManager.GetAvailableWorlds()
            .OfType<WorldData.World>()
            .SelectMany(world => world.dimensions.Values.OfType<WorldData.AbstractLevel>().Prepend(world))
            .Select(level => level.Generator)
            .OfType<WorldData.Generators.MojangGenerator>()
            .Where(generator => generator.Builder is not null)
            .ToList();

        var applied = PluginFeatures.Apply(
            this.plugins.SelectMany(plugin => plugin.FeatureFiles),
            this.plugins.SelectMany(plugin => plugin.CodecFiles),
            this.plugins.SelectMany(plugin => plugin.WorldgenTypes),
            generators.Select(generator => generator.Dimension.Identifier).ToHashSet(),
            this.logger);

        if (!applied)
            return;

        foreach (var generator in generators)
        {
            if (!generator.Builder.RefreshFeatures())
                Log.KeptPreviousFeatures(this.logger, generator.Dimension.Identifier);
        }
    }

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
    /// The services every plugin starts with, plus the singletons of the plugins it depends on.
    /// </summary>
    private ServiceCollection CreatePluginServices(ImmutableArray<PluginContainer> dependencies)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConfiguration(this.configuration);
        });

        services.AddSingleton(this.server);
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

        [LoggerMessage(Level = LogLevel.Error, Message = "Left out tag file {Path} of plugin {Source}")]
        public static partial void TagFileFailed(ILogger logger, Exception exception, string path, string source);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Left out data file {Path} of plugin {Source}")]
        public static partial void CodecFileFailed(ILogger logger, Exception exception, string path, string source);

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

        [LoggerMessage(Level = LogLevel.Error, Message = "Plugin {PluginName} failed while unloading")]
        public static partial void UnloadingFailed(ILogger logger, Exception exception, string pluginName);

        [LoggerMessage(Level = LogLevel.Error, Message = "Disposing plugin {PluginName} failed")]
        public static partial void DisposeFailed(ILogger logger, Exception exception, string pluginName);
    }
}

// thank you Roxxel && DorrianD3V for the invasion <3
// thank you Jonpro03 for your awesome contributions
// thank you Sebastian for your amazing plugin framework <3
// thank you Tides, Craftplacer for being part of the team early on <3
