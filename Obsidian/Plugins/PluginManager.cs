using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API.Logging;
using Obsidian.API.Plugins;
using Obsidian.Commands.Framework;
using Obsidian.Hosting;
using Obsidian.Plugins.PluginProviders;
using Obsidian.Plugins.ServiceProviders;
using Obsidian.Registries;

namespace Obsidian.Plugins;

public sealed class PluginManager
{
    private const string loadEvent = "OnLoad";

    internal readonly ILogger logger;

    private readonly List<PluginContainer> plugins = new();
    private readonly List<PluginContainer> stagedPlugins = new();
    private readonly IServiceProvider serverProvider;
    private readonly IServer server;

    private readonly IPluginRegistry pluginRegistry;
    private readonly IServiceCollection pluginServiceDescriptors = new ServiceCollection();

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

    public PluginManager(IServiceProvider serverProvider, IServer server, ILogger logger)
    {
        var env = serverProvider.GetRequiredService<IServerEnvironment>();

        this.server = server;
        this.logger = logger;
        this.serverProvider = serverProvider;
        this.pluginRegistry = new PluginRegistry(server);

        PluginProviderSelector.RemotePluginProvider = new RemotePluginProvider(logger);
        PluginProviderSelector.UncompiledPluginProvider = new UncompiledPluginProvider(logger);
        PluginProviderSelector.CompiledPluginProvider = new CompiledPluginProvider(logger);

        ConfigureInitialServices(env);

        DirectoryWatcher.FileChanged += (path) => Task.Run(async () =>
        {
            var old = plugins.FirstOrDefault(plugin => plugin.Source == path) ??
                stagedPlugins.FirstOrDefault(plugin => plugin.Source == path);

            if (old != null)
                await this.UnloadPluginAsync(old);

            await this.LoadPluginAsync(path);
        });
        DirectoryWatcher.FileRenamed += OnPluginSourceRenamed;
        DirectoryWatcher.FileDeleted += OnPluginSourceDeleted;
    }

    private void ConfigureInitialServices(IServerEnvironment env)
    {
        this.pluginServiceDescriptors.AddLogging((builder) =>
        {
            builder.ClearProviders();
            builder.AddProvider(new LoggerProvider(env.Configuration.LogLevel));
            builder.SetMinimumLevel(env.Configuration.LogLevel);
        });
        this.pluginServiceDescriptors.AddSingleton<IServerConfiguration>(x => env.Configuration);
    }

    /// <summary>
    /// Loads a plugin from selected path asynchronously.
    /// </summary>
    /// <param name="path">Path to load the plugin from. Can point either to local <b>DLL</b>, <b>C# code file</b> or a <b>GitHub project url</b>.</param>
    /// <returns>Loaded plugin. If loading failed, <see cref="PluginContainer.Plugin"/> property will be null.</returns>
    public async Task<PluginContainer?> LoadPluginAsync(string path)
    {
        var provider = PluginProviderSelector.GetPluginProvider(path);
        if (provider is null)
        {
            logger?.LogError("Couldn't load plugin from path '{path}'", path);
            return null;
        }

        PluginContainer plugin = await provider.GetPluginAsync(path).ConfigureAwait(false);

        return HandlePlugin(plugin);
    }

    private PluginContainer? HandlePlugin(PluginContainer pluginContainer)
    {
        if (pluginContainer?.Plugin is null)
        {
            return pluginContainer;
        }

        //Inject first wave of services (services initialized by obsidian e.x IServerConfiguration)
        PluginServiceHandler.InjectServices(this.serverProvider, pluginContainer, this.logger);

        pluginContainer.Plugin.unload = async () => await UnloadPluginAsync(pluginContainer);

        if (pluginContainer.IsReady)
        {
            lock (plugins)
            {
                plugins.Add(pluginContainer);
            }

            pluginContainer.Plugin.ConfigureServices(this.pluginServiceDescriptors);
            pluginContainer.Plugin.ConfigureRegistry(this.pluginRegistry);

            pluginContainer.Loaded = true;
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

    /// <summary>
    /// Will cause selected plugin to be unloaded asynchronously.
    /// </summary>
    public async Task UnloadPluginAsync(PluginContainer pluginContainer)
    {
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

        ((Server)this.server).CommandsHandler.UnregisterPluginCommands(pluginContainer);

        try
        {
            await pluginContainer.Plugin.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception occured when disposing {pluginName}", pluginContainer.Info.Name);
        }

        pluginContainer.LoadContext.Unload();
        pluginContainer.LoadContext.Unloading += _ => logger.LogInformation("Finished unloading {pluginName} plugin", pluginContainer.Info.Name);

        pluginContainer.Dispose();
    }

    public void ServerReady()
    {
        PluginServiceProvider ??= this.pluginServiceDescriptors.BuildServiceProvider(true);
        foreach (var pluginContainer in this.plugins)
        {
            if (!pluginContainer.Loaded)
                continue;

            pluginContainer.ServiceScope = this.PluginServiceProvider.CreateScope();

            pluginContainer.InjectServices(this.logger);

            InvokeOnLoad(pluginContainer);
        }

        //THis only needs to be called once 😭😭
        CommandsRegistry.Register((Server)server);
    }

    private void OnPluginStateChanged(PluginContainer plugin)
    {
        if (plugin.IsReady)
        {
            RunStaged(plugin);
        }
        else
        {
            StageRunning(plugin);
        }
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

    private void StageRunning(PluginContainer plugin)
    {
        lock (plugins)
        {
            if (!plugins.Remove(plugin))
                return;
        }

        lock (stagedPlugins)
        {
            stagedPlugins.Add(plugin);
        }
    }

    private void RunStaged(PluginContainer plugin)
    {
        lock (stagedPlugins)
        {
            if (!stagedPlugins.Remove(plugin))
                return;
        }

        lock (plugins)
        {
            plugins.Add(plugin);
        }

        if (!plugin.Loaded)
        {
            InvokeOnLoad(plugin);
            plugin.Loaded = true;
        }
    }

    private void InvokeOnLoad(PluginContainer plugin)
    {
        var task = plugin.Plugin.OnLoadAsync(this.server).AsTask();
        if (task.Status == TaskStatus.Created)
        {
            task.RunSynchronously();
        }
        if (task.Status == TaskStatus.Faulted)
        {
            logger?.LogError(task.Exception?.InnerException, "Invoking {pluginName}.{loadEvent} faulted.", plugin.Info.Name, loadEvent);
        }
    }
}

// thank you Roxxel && DorrianD3V for the invasion <3
// thank you Jonpro03 for your awesome contributions
// thank you Sebastian for your amazing plugin framework <3
// thank you Tides, Craftplacer for being part of the team early on <3
