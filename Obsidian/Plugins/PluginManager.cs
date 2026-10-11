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
using System.Security.Cryptography;
using System.Threading;

namespace Obsidian.Plugins;

/// <summary>
/// Loads, reloads and unloads plugins.
/// </summary>
/// <remarks>
/// <para>
/// At startup every plugin in the plugins folder loads, in dependency order, before the worlds do. Afterwards the
/// folder is watched: a changed file reloads its plugin, and a deleted one unloads it. A plugin's dependents reload
/// with it, since they use its types. Plugins whose dependencies are missing wait and load once they are satisfied.
/// </para>
/// <para>
/// Everything a plugin registers (commands, events, services) is removed when it unloads, so that its collectible load
/// context can unload too.
/// </para>
/// </remarks>
public sealed partial class PluginManager : IAsyncDisposable
{
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
    private readonly List<RSAParameters> acceptedKeys = [];

    private ImmutableArray<PluginContainer> plugins = [];
    private ImmutableArray<PluginArchive> stagedPlugins = [];
    private bool serverReady;

    public ImmutableArray<RSAParameters> AcceptedKeys => [.. this.acceptedKeys];

    /// <summary>
    /// The loaded plugins, in load order.
    /// <br/><b>Important note:</b> keeping references to plugin containers outside this class will make them unloadable.
    /// </summary>
    public IReadOnlyList<PluginContainer> Plugins => this.plugins;

    /// <summary>
    /// Plugins that couldn't load yet, e.g. because a required dependency is missing. They are tried again whenever
    /// another plugin loads.
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
        DirectoryWatcher.FileRenamed += this.OnPluginSourceRenamed;
        DirectoryWatcher.FileDeleted += path => _ = this.UnloadPluginAsync(path);
    }

    /// <summary>
    /// Loads every plugin in the plugins folder and starts watching it.
    /// </summary>
    public async Task LoadPluginsAsync()
    {
        try
        {
            await this.LoadAcceptedKeysAsync();

            var archives = new List<PluginArchive>();
            foreach (var file in Directory.GetFiles(ServerConstants.PluginsPath, "*.obby", SearchOption.AllDirectories))
            {
                if (await this.TryOpenAsync(file) is PluginArchive archive)
                    archives.Add(archive);
            }

            await this.changes.WaitAsync();
            try
            {
                await this.LoadArchivesAsync(archives);
            }
            finally
            {
                this.changes.Release();
            }

            DirectoryWatcher.Watch(ServerConstants.PluginsPath);
        }
        finally
        {
            this.startup?.Complete();
        }
    }

    public async Task UnloadPluginsAsync()
    {
        await this.changes.WaitAsync();
        try
        {
            foreach (var plugin in this.plugins.Reverse())
                await this.UnloadAsync(plugin);
        }
        finally
        {
            this.changes.Release();
        }
    }

    /// <summary>
    /// Loads a plugin file, or reloads it if it is loaded already.
    /// </summary>
    /// <param name="path">The plugin file.</param>
    /// <returns>The loaded plugin, or null if it couldn't load (or has to wait for its dependencies).</returns>
    public async Task<PluginContainer?> LoadPluginAsync(string path)
    {
        var archive = await this.TryOpenAsync(path);
        if (archive is null)
            return null;

        await this.changes.WaitAsync();
        try
        {
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

            return this.plugins.FirstOrDefault(plugin => plugin.Source == path);
        }
        finally
        {
            this.changes.Release();
        }
    }

    /// <summary>
    /// Unloads a plugin, and the plugins that depend on it.
    /// </summary>
    public async Task UnloadPluginAsync(PluginContainer pluginContainer)
    {
        await this.changes.WaitAsync();
        try
        {
            await this.UnloadAndStageDependentsAsync(pluginContainer);
        }
        finally
        {
            this.changes.Release();
        }
    }

    public async ValueTask OnServerReadyAsync()
    {
        await this.changes.WaitAsync();
        try
        {
            this.serverReady = true;

            foreach (var pluginContainer in this.plugins)
                await this.StartAsync(pluginContainer);
        }
        finally
        {
            this.changes.Release();
        }

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
        await this.UnloadPluginsAsync();

        this.DirectoryWatcher.Dispose();
    }

    private async Task UnloadPluginAsync(string path)
    {
        await this.changes.WaitAsync();
        try
        {
            this.stagedPlugins = this.stagedPlugins.RemoveAll(staged => staged.Source == path);

            if (this.plugins.FirstOrDefault(plugin => plugin.Source == path) is PluginContainer plugin)
                await this.UnloadAndStageDependentsAsync(plugin);
        }
        finally
        {
            this.changes.Release();
        }
    }

    // Dependents can't stay loaded without the plugin, so they wait for it to come back.
    private async Task UnloadAndStageDependentsAsync(PluginContainer pluginContainer)
    {
        if (!this.plugins.Contains(pluginContainer))
            return;

        foreach (var unloaded in await this.UnloadWithDependentsAsync(pluginContainer))
        {
            if (unloaded != pluginContainer)
                this.stagedPlugins = this.stagedPlugins.Add(unloaded.Archive);
        }

        this.OnPluginsChanged();
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

        this.OnPluginsChanged();
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
            Source = archive.Source,
            LoadContext = new PluginLoadContext(info.AssemblyName),
            Dependencies = dependencies
        };

        foreach (var dependency in dependencies)
            container.LoadContext.AddDependency(dependency.LoadContext);

        try
        {
            PluginAssemblyLoader.Load(container);

            // The plugin can use the server's services while it registers its own.
            PluginServiceHandler.InjectServices(this.serverProvider, container.Plugin!, this.logger);

            var services = this.CreatePluginServices();
            container.Plugin!.ConfigureServices(services);
            container.SetServices(services, this.serverProvider);

            // Registering looks the plugin up by its assembly, so it has to be in the list first.
            this.plugins = this.plugins.Add(container);

            container.Plugin.ConfigureRegistry(this.pluginRegistry);
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

    private async Task UnloadAsync(PluginContainer pluginContainer)
    {
        var name = pluginContainer.Info.Name;
        var stopwatch = Stopwatch.StartNew();

        this.commandHandler.UnregisterPluginCommands(pluginContainer);
        this.eventDispatcher.UnregisterPluginEvents(pluginContainer);

        if (pluginContainer.LoadContext is not null)
            this.commandHandler.UnregisterArgumentParsers(pluginContainer.LoadContext);

        if (this.server is Server concreteServer)
            concreteServer.PayloadChannels.UnregisterPluginPayloads(pluginContainer);

        if (pluginContainer.Plugin is PluginBase plugin)
        {
            try
            {
                if (pluginContainer.Loaded)
                    await plugin.OnUnloadingAsync();

                await plugin.DisposeAsync();
            }
            catch (Exception ex)
            {
                Log.DisposeFailed(this.logger, ex, name);
            }
        }

        this.plugins = this.plugins.Remove(pluginContainer);

        var loadContext = pluginContainer.LoadContext;

        // Disposing drops the container's references into the load context, which can't unload while they exist.
        pluginContainer.Dispose();

        stopwatch.Stop();

        loadContext.Unloading += _ => Log.Unloaded(this.logger, name, stopwatch.ElapsedMilliseconds);
        loadContext.Unload();
    }

    // Players see the commands of the plugins that are loaded now.
    private void OnPluginsChanged()
    {
        if (!this.serverReady)
            return;

        CommandsRegistry.Register((Server)this.server);

        foreach (var player in this.server.OnlinePlayers.Values)
            player.SendPacket(CommandsRegistry.Packet);
    }

    private ServiceCollection CreatePluginServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConfiguration(this.configuration);
        });

        services.AddSingleton(this.serverProvider.GetRequiredService<IOptionsMonitor<ServerConfiguration>>());

        return services;
    }

    private async Task<PluginArchive?> TryOpenAsync(string path)
    {
        PluginArchive archive;
        try
        {
            archive = await PluginArchive.OpenAsync(path, this.acceptedKeys);
        }
        catch (Exception ex)
        {
            Log.LoadFailed(this.logger, ex, path);
            return null;
        }

        if (!archive.ValidSignature && !this.server.Configuration.AllowUntrustedPlugins)
        {
            Log.Untrusted(this.logger, path);
            return null;
        }

        return archive;
    }

    private async Task LoadAcceptedKeysAsync()
    {
        this.acceptedKeys.Clear();

        foreach (var keyFile in Directory.GetFiles(ServerConstants.AcceptedKeysPath))
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(await File.ReadAllTextAsync(keyFile));

            this.acceptedKeys.Add(rsa.ExportParameters(false));
        }
    }

    private void OnPluginSourceRenamed(string oldSource, string newSource)
    {
        var renamedPlugin = this.plugins.FirstOrDefault(plugin => plugin.Source == oldSource);
        if (renamedPlugin is not null)
            renamedPlugin.Source = newSource;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information,
            Message = "Loaded plugin {PluginName} {Version} in {ElapsedMilliseconds}ms")]
        public static partial void Loaded(ILogger logger, string pluginName, Version version, long elapsedMilliseconds);

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load plugin {Path}")]
        public static partial void LoadFailed(ILogger logger, Exception exception, string path);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Plugin {PluginName} can't load yet: {Reason}")]
        public static partial void Staged(ILogger logger, string pluginName, string reason);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{Warning}")]
        public static partial void DependencyWarning(ILogger logger, string warning);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping plugin {PluginName}: it only runs on the {Side} side")]
        public static partial void WrongSide(ILogger logger, string pluginName, PluginSide side);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Skipping plugin {Path}: it isn't signed by an accepted key and untrusted plugins aren't allowed")]
        public static partial void Untrusted(ILogger logger, string path);

        [LoggerMessage(Level = LogLevel.Information, Message = "Unloaded plugin {PluginName} in {ElapsedMilliseconds}ms")]
        public static partial void Unloaded(ILogger logger, string pluginName, long elapsedMilliseconds);

        [LoggerMessage(Level = LogLevel.Error, Message = "Disposing plugin {PluginName} failed")]
        public static partial void DisposeFailed(ILogger logger, Exception exception, string pluginName);
    }
}

// thank you Roxxel && DorrianD3V for the invasion <3
// thank you Jonpro03 for your awesome contributions
// thank you Sebastian for your amazing plugin framework <3
// thank you Tides, Craftplacer for being part of the team early on <3
