using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API.Logging;
using Obsidian.Commands.Framework;
using Obsidian.Events;
using Obsidian.Hosting;
using Obsidian.Plugins.PluginProviders;
using Obsidian.Plugins.ServiceProviders;
using Obsidian.Registries;
using System;

namespace Obsidian.Plugins;

public sealed class PluginManager
{
    private const string loadEvent = "OnLoad";

    internal readonly ILogger logger;
    internal readonly ILoggerProvider loggerProvider;

    private readonly List<PluginContainer> plugins = new();
    private readonly List<PluginContainer> stagedPlugins = new();
    private readonly List<EventContainer> events = new();
    private readonly IServiceProvider serverProvider;
    private readonly object eventSource;
    private readonly IServer server;

    private readonly CommandHandler commands;
    
    private IServiceCollection serviceCollection = new ServiceCollection();

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

    public PluginManager(IServiceProvider serverProvider, object eventSource, IServer server, ILogger logger, CommandHandler commands)
    {
        var env = serverProvider.GetRequiredService<IServerEnvironment>();

        this.server = server;
        this.logger = logger;
        this.serverProvider = serverProvider;
        this.eventSource = eventSource;
        this.commands = commands;
        this.loggerProvider = new LoggerProvider(env.Configuration.LogLevel);

        ConfigureInitialServices(env);

        DirectoryWatcher.FileChanged += (path) => Task.Run(async () =>
        {
            var old = plugins.FirstOrDefault(plugin => plugin.Source == path) ??
                stagedPlugins.FirstOrDefault(plugin => plugin.Source == path);

            if (old != null)
                await this.UnloadPluginAsync(old);

            LoadPlugin(path);
        });
        DirectoryWatcher.FileRenamed += OnPluginSourceRenamed;
        DirectoryWatcher.FileDeleted += OnPluginSourceDeleted;

        if (eventSource != null)
            GetEvents(eventSource);
    }

    private void ConfigureInitialServices(IServerEnvironment env)
    {
        this.serviceCollection.AddLogging((builder) =>
        {
            builder.ClearProviders();
            builder.AddProvider(this.loggerProvider);
            builder.SetMinimumLevel(env.Configuration.LogLevel);
        });
        this.serviceCollection.AddSingleton<IServerConfiguration>(x => env.Configuration);
    }

    /// <summary>
    /// Loads a plugin from selected path.
    /// <br/><b>Important note:</b> keeping references to plugin containers outside this class will make them unloadable.
    /// </summary>
    /// <param name="path">Path to load the plugin from. Can point either to local <b>DLL</b>, <b>C# code file</b> or a <b>GitHub project url</b>.</param>
    /// <returns>Loaded plugin. If loading failed, <see cref="PluginContainer.Plugin"/> property will be null.</returns>
    public PluginContainer? LoadPlugin(string path)
    {
        IPluginProvider provider = PluginProviderSelector.GetPluginProvider(path);
        if (provider is null)
        {
            logger?.LogError("Couldn't load plugin from path '{path}'", path);
            return null;
        }

        PluginContainer plugin = provider.GetPlugin(path, logger);

        return HandlePlugin(plugin);
    }

    /// <summary>
    /// Loads a plugin from selected path asynchronously.
    /// </summary>
    /// <param name="path">Path to load the plugin from. Can point either to local <b>DLL</b>, <b>C# code file</b> or a <b>GitHub project url</b>.</param>
    /// <returns>Loaded plugin. If loading failed, <see cref="PluginContainer.Plugin"/> property will be null.</returns>
    public async Task<PluginContainer?> LoadPluginAsync(string path)
    {
        IPluginProvider provider = PluginProviderSelector.GetPluginProvider(path);
        if (provider is null)
        {
            logger?.LogError("Couldn't load plugin from path '{path}'", path);
            return null;
        }

        PluginContainer plugin = await provider.GetPluginAsync(path, logger).ConfigureAwait(false);

        return HandlePlugin(plugin);
    }

    private PluginContainer? HandlePlugin(PluginContainer pluginContainer)
    {
        if (pluginContainer?.Plugin is null)
        {
            return pluginContainer;
        }

        //Inject first wave of services (services initialized by obsidian e.x IServerConfiguration)
        PluginServiceHandler.InjectServices(this.serverProvider, pluginContainer, this.logger, this.loggerProvider);

        pluginContainer.RegisterDependencies(this, logger);

        pluginContainer.Plugin.unload = async () => await UnloadPluginAsync(pluginContainer);

        if (pluginContainer.IsReady)
        {
            lock (plugins)
            {
                plugins.Add(pluginContainer);
            }

            pluginContainer.Plugin.Configure(serviceCollection);

            //TODO move this so this is called by the plugin and not the manager.
            this.commands.RegisterCommands(pluginContainer);

            pluginContainer.Loaded = true;
            ExposePluginAsDependency(pluginContainer);
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

        commands.UnregisterPluginCommands(pluginContainer);

        UnregisterEvents(pluginContainer);

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
        PluginServiceProvider ??= this.serviceCollection.BuildServiceProvider(true);
        foreach(var pluginContainer in this.plugins)
        {
            if (!pluginContainer.Loaded)
                continue;

            RegisterEvents(pluginContainer);

            PluginServiceHandler.InjectServices(PluginServiceProvider, pluginContainer, logger, loggerProvider);
           
            CommandsRegistry.Register((Server)server);

            InvokeOnLoad(pluginContainer);
        }
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

    private void OnPluginSourceDeleted(string path)
    {
        var deletedPlugin = plugins.FirstOrDefault(plugin => plugin.Source == path) ?? stagedPlugins.FirstOrDefault(plugin => plugin.Source == path);
        if (deletedPlugin != null)
            UnloadPluginAsync(deletedPlugin);
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

        UnregisterEvents(plugin);
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

        RegisterEvents(plugin);
        ExposePluginAsDependency(plugin);
    }

    private void GetEvents(object eventSource)
    {
        var sourceType = eventSource.GetType();
        foreach (var fieldInfo in sourceType.GetFields())
        {
            var field = fieldInfo.GetValue(eventSource) as IEventRegistry;
            if (field is not null && field.Name is not null)
            {
                events.Add(new EventContainer($"On{field.Name}", field));
            }
        }
    }

    private void RegisterEvents(PluginContainer plugin)
    {
        var pluginType = plugin.Plugin.GetType();
        foreach (var @event in events)
        {
            var handler = pluginType.GetMethod(@event.Name);
            if (handler is not null && @event.EventRegistry.TryRegisterEvent(handler, plugin.Plugin, out var @delegate))
            {
                plugin.EventHandlers.Add(@event, @delegate);
            }
        }
    }

    private void UnregisterEvents(PluginContainer plugin)
    {
        foreach (var @event in events)
        {
            if (plugin.EventHandlers.TryGetValue(@event, out var handler))
            {
                @event.EventRegistry.UnregisterEvent(handler);
                plugin.EventHandlers.Remove(@event);
            }
        }
    }

    private void ExposePluginAsDependency(PluginContainer plugin)
    {
        lock (plugins)
        {
            foreach (var other in plugins)
            {
                other.TryAddDependency(plugin, logger);
            }
        }

        lock (stagedPlugins)
        {
            for (int i = 0; i < stagedPlugins.Count; i++)
            {
                var other = stagedPlugins[i];
                if (other.TryAddDependency(plugin, logger!))
                {
                    OnPluginStateChanged(other);
                    if (other.Loaded)
                    {
                        i--;
                        logger?.LogDebug("Plugin {pluginName} unstaged. Required dependencies were supplied.", other.Info.Name);
                    }
                }
            }
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
