using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using Obsidian.API.Plugins;
using Obsidian.Commands.Framework;
using Obsidian.Hosting;
using Obsidian.Plugins.PluginProviders;
using Obsidian.Plugins.ServiceProviders;
using Obsidian.Registries;
using Obsidian.Services;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace Obsidian.Plugins;

public sealed class PluginManager
{
    internal readonly ILogger logger;
    private readonly IConfiguration configuration;
    internal readonly IServer server;

    private static PackedPluginProvider packedPluginProvider = default!;

    private readonly List<PluginContainer> plugins = [];
    private readonly List<PluginContainer> stagedPlugins = [];
    private readonly List<RSAParameters> acceptedKeys = [];

    private readonly IServiceProvider serverProvider;
    private readonly CommandHandler commandHandler;
    private readonly IPluginRegistry pluginRegistry;
    private readonly IServiceCollection pluginServiceDescriptors = new ServiceCollection();

    public ImmutableArray<RSAParameters> AcceptedKeys => acceptedKeys.ToImmutableArray();

    /// <summary>
    /// List of all loaded plugins.
    /// <br/><b>Important note:</b> keeping references to plugin containers outside this class will make them unloadable.
    /// </summary>
    public IReadOnlyList<PluginContainer> Plugins => plugins;

    /// <summary>
    /// List of all staged plugins.
    /// <br/><b>Important note:</b> keeping references to plugin containers outside this class will make them unloadable.
    /// </summary>
    public IReadOnlyList<PluginContainer> StagedPlugins => stagedPlugins;

    /// <summary>
    /// Utility class, responding to file changes inside watched directories.
    /// </summary>
    public DirectoryWatcher DirectoryWatcher { get; } = new();

    public IServiceProvider PluginServiceProvider { get; private set; } = default!;

    public PluginManager(IServiceProvider serverProvider, IServer server,
        EventDispatcher eventDispatcher, CommandHandler commandHandler, ILogger logger, IConfiguration configuration)
    {
        var env = serverProvider.GetRequiredService<IServerEnvironment>();

        this.server = server;
        this.commandHandler = commandHandler;
        this.logger = logger;
        this.configuration = configuration;
        this.serverProvider = serverProvider;
        this.pluginRegistry = new PluginRegistry(this, eventDispatcher, commandHandler, logger);

        packedPluginProvider = new(this, logger);

        ConfigureInitialServices();

        DirectoryWatcher.Filters = [".obby"];
        DirectoryWatcher.FileChanged += async (path) =>
        {
            var old = plugins.FirstOrDefault(plugin => plugin.Source == path) ??
                stagedPlugins.FirstOrDefault(plugin => plugin.Source == path);

            if (old != null)
                await this.UnloadPluginAsync(old);

            await this.LoadPluginAsync(path);
        };
        DirectoryWatcher.FileRenamed += OnPluginSourceRenamed;
        DirectoryWatcher.FileDeleted += OnPluginSourceDeleted;
    }

    public async Task LoadPluginsAsync()
    {
        //TODO talk about what format we should support
        // get directory surrent dll is in
        var acceptedKeysPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "accepted_keys"); 
        var acceptedKeyFiles = Directory.GetFiles(acceptedKeysPath);

        using var rsa = RSA.Create();
        foreach (var certFile in acceptedKeyFiles)
        {
            var xml = await File.ReadAllTextAsync(certFile);
            rsa.FromXmlString(xml);

            this.acceptedKeys.Add(rsa.ExportParameters(false));
        }

        var files = Directory.GetFiles("plugins", "*.obby", SearchOption.AllDirectories);

        var waitingForDepend = new List<PluginContainer>();
        foreach (var file in files)
        {
            var pluginContainer = await this.LoadPluginAsync(file);

            if (pluginContainer is null)
                continue;

            foreach (var canLoad in waitingForDepend.Where(x => x.IsDependency(pluginContainer.Info.Id)).ToList())
            {
                packedPluginProvider.InitializePlugin(canLoad);

                //Add dependency to plugin
                canLoad.AddDependency(pluginContainer.LoadContext);

                await this.HandlePluginAsync(canLoad);

                waitingForDepend.Remove(canLoad);
            }

            if (pluginContainer.Plugin is null)
                waitingForDepend.Add(pluginContainer);
        }

        DirectoryWatcher.Watch("plugins");
    }

    /// <summary>
    /// Loads a plugin from selected path asynchronously.
    /// </summary>
    /// <param name="path">Path to load the plugin from. Can point either to local <b>OBBY</b> or <b>DLL</b>.</param>
    /// <returns>Loaded plugin. If loading failed, <see cref="PluginContainer.Plugin"/> property will be null.</returns>
    public async Task<PluginContainer?> LoadPluginAsync(string path)
    {
        try
        {
            var plugin = await packedPluginProvider.GetPluginAsync(path).ConfigureAwait(false);

            return plugin is null ? null : await HandlePluginAsync(plugin);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Failed to load plugin.");//TODO DEFAULT LOGGER DOES NOT SUPPORT EXCEPTIONS

            throw;
        }
    }

    /// <summary>
    /// Will cause selected plugin to be unloaded asynchronously.
    /// </summary>
    public async Task UnloadPluginAsync(PluginContainer pluginContainer)
    {
        this.logger.LogInformation("Unloading plugin...");

        bool removed = false;
        lock (plugins)
        {
            removed = plugins.Remove(pluginContainer);
        }

        if (!removed)
        {
            lock (stagedPlugins)
            {
                stagedPlugins.Remove(pluginContainer);
            }
        }

        this.commandHandler.UnregisterPluginCommands(pluginContainer);

        var stopwatch = Stopwatch.StartNew();

        await pluginContainer.Plugin.OnUnloadingAsync();

        try
        {
            await pluginContainer.Plugin.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception occured when disposing {pluginName}", pluginContainer.Info.Name);
        }

        var loadContext = pluginContainer.LoadContext;

        //Dispose has to be called before the LoadContext can unload.
        pluginContainer.Dispose();

        stopwatch.Stop();

        loadContext.Unloading += _ => logger.LogInformation("Finished unloading {pluginName} plugin in  {timer}ms", pluginContainer.Info.Name, stopwatch.ElapsedMilliseconds);
        loadContext.Unload();
    }

    public async ValueTask OnServerReadyAsync()
    {
        PluginServiceProvider ??= this.pluginServiceDescriptors.BuildServiceProvider(true);
        foreach (var pluginContainer in this.plugins)
        {
            if (!pluginContainer.Loaded)
                continue;

            pluginContainer.ServiceScope = this.PluginServiceProvider.CreateScope();

            pluginContainer.InjectServices(this.logger);

            await pluginContainer.Plugin.OnServerReadyAsync(this.server);
        }

        //THis only needs to be called once 😭😭
        CommandsRegistry.Register((Server)server);
    }

    /// <summary>
    /// Gets the PluginContainer either by specified assembly or by current executing assembly.
    /// </summary>
    /// <param name="assembly">The assembly you want to use to find the plugin container.</param>
    public PluginContainer GetPluginContainerByAssembly(Assembly? assembly = null) =>
        this.Plugins.First(x => x.PluginAssembly == (assembly ?? Assembly.GetCallingAssembly()));

    private void ConfigureInitialServices()
    {
        this.pluginServiceDescriptors.AddLogging((builder) =>
        {
            builder.ClearProviders();
            builder.AddConfiguration(this.configuration);
        });
        this.pluginServiceDescriptors.AddSingleton(serverProvider.GetRequiredService<IOptionsMonitor<ServerConfiguration>>());
    }

    private async ValueTask<PluginContainer> HandlePluginAsync(PluginContainer pluginContainer)
    {
        //The plugin still hasn't fully loaded. Probably due to it having a hard dependency
        if (pluginContainer.Plugin is null)
            return pluginContainer;

        //Inject first wave of services (services initialized by obsidian e.x IServerConfiguration)
        PluginServiceHandler.InjectServices(this.serverProvider, pluginContainer, this.logger);

        if (pluginContainer.IsReady)
        {
            lock (plugins)
            {
                plugins.Add(pluginContainer);
            }

            pluginContainer.Plugin.ConfigureServices(this.pluginServiceDescriptors);
            pluginContainer.Plugin.ConfigureRegistry(this.pluginRegistry);

            pluginContainer.Loaded = true;

            await pluginContainer.Plugin.OnLoadedAsync(this.server);
        }
        else
        {
            lock (stagedPlugins)
            {
                stagedPlugins.Add(pluginContainer);
            }

            if (logger != null)
            {
                var stageMessage = new System.Text.StringBuilder(50);
                stageMessage.Append($"Plugin {pluginContainer.Info.Name} staged");
                if (!pluginContainer.HasDependencies)
                    stageMessage.Append(", missing dependencies");

                logger.LogWarning("{}", stageMessage.ToString());
            }
        }

        logger?.LogInformation("Loading finished!");

        return pluginContainer;
    }

    private void OnPluginSourceRenamed(string oldSource, string newSource)
    {
        var renamedPlugin = plugins.FirstOrDefault(plugin => plugin.Source == oldSource) ?? stagedPlugins.FirstOrDefault(plugin => plugin.Source == oldSource);
        if (renamedPlugin != null)
            renamedPlugin.Source = newSource;
    }

    private async void OnPluginSourceDeleted(string path)
    {
        var deletedPlugin = plugins.FirstOrDefault(plugin => plugin.Source == path) ?? stagedPlugins.FirstOrDefault(plugin => plugin.Source == path);
        if (deletedPlugin != null)
            await UnloadPluginAsync(deletedPlugin);
    }
}

// thank you Roxxel && DorrianD3V for the invasion <3
// thank you Jonpro03 for your awesome contributions
// thank you Sebastian for your amazing plugin framework <3
// thank you Tides, Craftplacer for being part of the team early on <3
