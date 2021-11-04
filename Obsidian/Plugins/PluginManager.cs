using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using Obsidian.API.Plugins.Events;
using Obsidian.API.Plugins.Services;
using Obsidian.Commands.Framework;
using Obsidian.Events;
using Obsidian.Plugins.Services;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Obsidian.Plugins
{
    public sealed class PluginManager : IDisposable
    {
        private List<Plugin> plugins = new();

        /// <summary>
        /// List of all loaded plugins.
        /// </summary>
        public ImmutableList<Plugin> Plugins => plugins.ToImmutableList();

        internal readonly IPluginServiceProvider PluginServiceProvider;
        private readonly Dictionary<string, SortedSet<EventContainer>> eventMap = new();
        private readonly ILogger logger;
        internal readonly Server server;
        private readonly CommandHandler commands;

        public PluginManager(Server server, ILogger logger, CommandHandler commands)
        {
            this.server = server;
            this.logger = logger;
            this.commands = commands;
            PluginServiceProvider = new PluginServiceProvider(this);
        }

        public void LoadFrom(DirectoryInfo directory)
        {
            if (!directory.Exists)
                throw new DirectoryNotFoundException();

            foreach (var fileInfo in directory.EnumerateFiles("*.dll", SearchOption.AllDirectories)) LoadFrom(fileInfo);
        }

        public void LoadFrom(FileInfo file)
        {
            var asm = LoadAssembly(file);

            foreach (var pluginType in asm.GetTypes().Where(t => t.IsAssignableTo(typeof(Plugin)) && !t.IsAbstract))
            {
                var plugin = (Plugin)Activator.CreateInstance(pluginType);
                plugins.Add(plugin);
            }
        }

        private static Assembly LoadAssembly(FileInfo file)
        {
            if (!file.Exists)
                throw new FileNotFoundException();
            
            // GetRawSymbolStore allows to print detailed stack trace on exception
            return Assembly.Load(File.ReadAllBytes(file.FullName), GetRawSymbolStore(file));
        }

        private static byte[] GetRawSymbolStore(FileInfo file)
        {
            file = new FileInfo(Path.ChangeExtension(file.FullName, "pdb"));

            return file.Exists ? File.ReadAllBytes(file.FullName) : Array.Empty<byte>();
        }

        public void InitializePlugins()
        {
            var map = (
                from plugin in plugins
                from dependency in plugin.Dependencies
                select new Tuple<string, string>(dependency.Identifier, plugin.Identifier)).ToList();

            plugins = TopologicalSort(plugins.Select(x => x.Identifier), map)
                .Select(x => plugins.First(p => p.Identifier == x)).ToList();

            foreach (var plugin in plugins)
            foreach (var dependency in plugin.Dependencies)
            {
                var depPlugin = plugins.Find(x => x.Identifier == dependency.Identifier);
                if (depPlugin is null)
                    throw new NullReferenceException($"TargetObject {dependency.Identifier} not found");

                if (!dependency.IsCorrectVersion(depPlugin.Info.Version))
                    throw new VersionNotFoundException(
                        $"TargetObject {depPlugin.Info.Name} matching version {dependency.Version} not found");
            }

            foreach (var plugin in plugins)
            {
                plugin.Initialize(PluginServiceProvider);
                RegisterEvents(plugin);
            }
        }

        public void DeInitializePlugins()
        {
            foreach (var plugin in plugins)
            {
                plugin.DeInitialize();
                UnregisterEvents(plugin);
            }
            eventMap.Clear();

            plugins.Clear();
        }
        
        /// <summary>
        /// Topological Sorting (Kahn's algorithm).
        /// <para>Shamelessly stolen from https://gist.github.com/Sup3rc4l1fr4g1l1571c3xp14l1d0c10u5/3341dba6a53d7171fe3397d13d00ee3f</para>
        /// </summary>
        /// <remarks>https://en.wikipedia.org/wiki/Topological_sorting</remarks>
        /// <typeparam name="T">Sorted type</typeparam>
        /// <param name="nodes">All nodes of directed acyclic graph.</param>
        /// <param name="edges">All edges of directed acyclic graph.</param>
        /// <returns>Sorted node in topological order.</returns>
        private static IEnumerable<T> TopologicalSort<T>(IEnumerable<T> nodes, ICollection<Tuple<T, T>> edges) where T : IEquatable<T> {
            // Empty list that will contain the sorted elements
            var l = new List<T>();

            // Set of all nodes with no incoming edges
            var s = new HashSet<T>(nodes.Where(n => edges.All(e => e.Item2.Equals(n) == false)));

            // while S is non-empty do
            while (s.Any()) {

                //  remove a node n from S
                var n = s.First();
                s.Remove(n);

                // add n to tail of L
                l.Add(n);

                // for each node m with an edge e from n to m do
                foreach (var e in edges.Where(e => e.Item1.Equals(n)).ToList()) {
                    var m = e.Item2;

                    // remove edge e from the graph
                    edges.Remove(e);

                    // if m has no other incoming edges then
                    if (edges.All(me => me.Item2.Equals(m) == false)) {
                        // insert m into S
                        s.Add(m);
                    }
                }
            }

            // if graph has edges then throw
            return edges.Any() ? throw new InvalidOperationException($"Cyclic dependency detected in [{string.Join(", ", edges.Select(t => $"({t.Item2}: {t.Item1})"))}]") : l;
        }

        public void InvokeEvent(string @event, AsyncEventArgs eventArgs)
        {
            if (!eventMap.TryGetValue(@event, out var events)) return;
            
            foreach (var container in events)
            {
                if (eventArgs.Handled) return;
                try
                {
                    container.Method.Invoke(container.TargetObject, new object[] {eventArgs});
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error executing event {Event} in plugin {Identifier}", @event, ((Plugin) container.TargetObject).Identifier);
                }
            }
        }

        public async Task InvokeEventAsync(string @event, AsyncEventArgs eventArgs)
        {
            if (!eventMap.TryGetValue(@event, out var events)) return;
            
            foreach (var container in events)
            {
                if (eventArgs.Handled) return;
                try
                {
                    if (IsAsync(container.Method))
                    {
                        var task = (Task)container.Method.Invoke(container.TargetObject, new object[] {eventArgs});
                        if (task is not null) await task;
                    }
                    else
                        container.Method.Invoke(container.TargetObject, new object[] {eventArgs});
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error executing event {Event} in plugin {Identifier}", @event, ((Plugin) container.TargetObject).Identifier);
                }
            }
        }

        private static bool IsAsync(MemberInfo methodInfo) => methodInfo.GetCustomAttribute<AsyncStateMachineAttribute>() != null;

        private void RegisterEvents(Plugin plugin)
        {
            foreach (var methodInfo in plugin.GetType().GetMethods())
            {
                var attr = methodInfo.GetCustomAttribute<EventHandlerAttribute>();
                if (attr is null)
                    continue;

                if (!eventMap.ContainsKey(attr.Event))
                    eventMap.Add(attr.Event, new SortedSet<EventContainer>(new EventContainerComparer()));

                eventMap[attr.Event].Add(new EventContainer(attr.Priority, methodInfo, plugin));
            }
        }

        private void UnregisterEvents(Plugin plugin)
        {
            foreach (var key in eventMap.Keys) eventMap[key].RemoveWhere(x => (Plugin)x.TargetObject == plugin);
        }

        public void Dispose()
        {
            DeInitializePlugins();
            PluginServiceProvider?.Dispose();
        }
    }
}

// thank you Roxxel && DorrianD3V for the invasion <3
// thank you Jonpro03 for your awesome contributions
// thank you Sebastian for your amazing plugin framework <3
// thank you Tides, Craftplacer for being part of the team early on <3