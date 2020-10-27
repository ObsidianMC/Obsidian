using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using Obsidian.Plugins.PluginProviders;
using Obsidian.Plugins.ServiceProviders;
using Obsidian.Util.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Plugins
{
    public class PluginManager
    {
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
        public DirectoryWatcher DirectoryWatcher { get; } = new DirectoryWatcher();

        private readonly List<PluginContainer> plugins = new List<PluginContainer>();
        private readonly List<PluginContainer> stagedPlugins = new List<PluginContainer>();
        private readonly ServiceProvider serviceProvider = ServiceProvider.Create();
        private readonly object eventSource;
        private readonly List<EventContainer> events = new List<EventContainer>();
        private readonly ILogger logger;

        private const string loadEvent = "OnLoad";

        public PluginManager() : this(null, null)
        {
        }

        public PluginManager(object eventSource) : this(eventSource, null)
        {
        }

        public PluginManager(object eventSource, ILogger logger)
        {
            this.logger = logger;
            this.eventSource = eventSource;

            DirectoryWatcher.FileChanged += (path) =>
            {
                var old = plugins.FirstOrDefault(plugin => plugin.Source == path) ??
                    stagedPlugins.FirstOrDefault(plugin => plugin.Source == path);
                if (old != null)
                    UnloadPlugin(old);

                LoadPlugin(path);
            };
            DirectoryWatcher.FileRenamed += OnPluginSourceRenamed;
            DirectoryWatcher.FileDeleted += OnPluginSourceDeleted;

            if (eventSource != null)
                GetEvents(eventSource);
        }

        /// <summary>
        /// Loads a plugin from selected path.
        /// <br/><b>Important note:</b> keeping references to plugin containers outside this class will make them unloadable.
        /// </summary>
        /// <param name="path">Path to load the plugin from. Can point either to local <b>DLL</b>, <b>C# code file</b> or a <b>GitHub project url</b>.</param>
        /// <param name="permissions">Permissions granted to the plugin.</param>
        /// <returns>Loaded plugin. If loading failed, <see cref="PluginContainer.Plugin"/> property will be null.</returns>
        public PluginContainer LoadPlugin(string path, PluginPermissions permissions = PluginPermissions.None)
        {
            IPluginProvider provider = PluginProviderSelector.GetPluginProvider(path);
            PluginContainer plugin = provider.GetPlugin(path, logger);

            return HandlePlugin(plugin, permissions);
        }

        /// <summary>
        /// Loads a plugin from selected path asynchronously.
        /// </summary>
        /// <param name="path">Path to load the plugin from. Can point either to local <b>DLL</b>, <b>C# code file</b> or a <b>GitHub project url</b>.</param>
        /// <param name="permissions">Permissions granted to the plugin.</param>
        /// <returns>Loaded plugin. If loading failed, <see cref="PluginContainer.Plugin"/> property will be null.</returns>
        public async Task<PluginContainer> LoadPluginAsync(string path, PluginPermissions permissions = PluginPermissions.None)
        {
            IPluginProvider provider = PluginProviderSelector.GetPluginProvider(path);
            if (provider == null)
            {
                logger?.LogError($"Couldn't load plugin from path '{path}'");
                return null;
            }

            PluginContainer plugin = await provider.GetPluginAsync(path, logger).ConfigureAwait(false);

            return HandlePlugin(plugin, permissions);
        }

        private PluginContainer HandlePlugin(PluginContainer plugin, PluginPermissions permissions)
        {
            if (plugin?.Plugin == null)
            {
                return plugin;
            }

            serviceProvider.InjectServices(plugin, logger);

            plugin.RegisterDependencies(this, logger);

            plugin.Permissions = permissions;
            plugin.PermissionsChanged += OnPluginStateChanged;

            if (plugin.IsReady)
            {
                lock (plugins)
                {
                    plugins.Add(plugin);
                }
                RegisterEvents(plugin);
                plugin.Plugin.InvokeAsync(loadEvent).TryRunSynchronously();
                plugin.Loaded = true;
                ExposePluginAsDependency(plugin);
            }
            else
            {
                lock (stagedPlugins)
                {
                    stagedPlugins.Add(plugin);
                }

                if (logger != null)
                {
                    var stageMessage = new System.Text.StringBuilder(50);
                    stageMessage.Append($"Plugin {plugin.Info.Name} staged");
                    if (!plugin.HasPermissions)
                        stageMessage.Append(", missing permissions");
                    if (!plugin.HasDependencies)
                        stageMessage.Append(", missing dependencies");

                    logger.LogWarning(stageMessage.ToString());
                }
            }

            logger?.LogInformation("Loading finished!");

            return plugin;
        }

        /// <summary>
        /// Will cause selected plugin to be unloaded.
        /// </summary>
        public void UnloadPlugin(PluginContainer plugin)
        {
            bool removed = false;
            lock (plugins)
            {
                removed = plugins.Remove(plugin);
            }

            if (!removed)
            {
                lock (stagedPlugins)
                {
                    stagedPlugins.Remove(plugin);
                }
            }

            UnregisterEvents(plugin);

            foreach (var service in plugin.DisposableServices)
            {
                service.Dispose();
            }

            if (plugin.Plugin is IDisposable disposable)
            {
                disposable.Dispose();
            }

            plugin.LoadContext.Unload();
            plugin.LoadContext.Unloading += _ => logger?.LogInformation($"Finished unloading {plugin.Info.Name} plugin");
        }

        /// <summary>
        /// Will cause selected plugin to be unloaded asynchronously.
        /// </summary>
        public async Task UnloadPluginAsync(PluginContainer plugin)
        {
            await Task.Run(() => UnloadPlugin(plugin));
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
                UnloadPlugin(deletedPlugin);
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
                plugin.Plugin.InvokeAsync(loadEvent).TryRunSynchronously();
                plugin.Loaded = true;
            }

            RegisterEvents(plugin);
            ExposePluginAsDependency(plugin);
        }

        private void GetEvents(object eventSource)
        {
            var sourceType = eventSource.GetType();
            foreach (var @event in sourceType.GetEvents())
            {
                events.Add(new EventContainer($"On{@event.Name}", @event));
            }
        }

        private void RegisterEvents(PluginContainer plugin)
        {
            var pluginType = plugin.Plugin.GetType();
            foreach (var @event in events)
            {
                var handler = pluginType.GetMethod(@event.Name);
                if (handler != null)
                {
                    var @delegate = Delegate.CreateDelegate(@event.Event.EventHandlerType, plugin.Plugin, handler, throwOnBindFailure: false);
                    if (@delegate != null)
                    {
                        @event.Event.AddEventHandler(eventSource, @delegate);
                        plugin.EventHandlers.Add(@event, @delegate);
                    }
                }
            }
        }

        private void UnregisterEvents(PluginContainer plugin)
        {
            foreach (var @event in events)
            {
                if (plugin.EventHandlers.TryGetValue(@event, out var handler))
                {
                    @event.Event.RemoveEventHandler(plugin, handler);
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
                    if (other.TryAddDependency(plugin, logger))
                    {
                        OnPluginStateChanged(other);
                        if (other.Loaded)
                        {
                            i--;
                            logger?.LogDebug($"Plugin {other.Info.Name} unstaged. Required dependencies were supplied.");
                        }
                    }
                }
            }
        }
    }
}

// thank you Roxxel && artemD3V for the invasion <3
