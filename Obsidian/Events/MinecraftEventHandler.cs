using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.API.Plugins.Events;
using Obsidian.Plugins;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Obsidian.Events
{
    public class MinecraftEventHandler
    {
        private readonly PluginManager pluginManager;
        private readonly ILogger logger;
        private readonly Dictionary<string, SortedSet<EventContainer>> eventMap = new();

        public MinecraftEventHandler(PluginManager pluginManager, ILogger logger)
        {
            this.pluginManager = pluginManager;
            this.logger = logger;
        }

        public void RegisterEvents(object source)
        {
            foreach (var methodInfo in source.GetType().GetMethods())
            {
                var attr = methodInfo.GetCustomAttribute<EventHandlerAttribute>();
                if (attr is null)
                    continue;

                if (!eventMap.ContainsKey(attr.Event))
                    eventMap.Add(attr.Event, new SortedSet<EventContainer>(new EventContainerComparer()));

                eventMap[attr.Event].Add(new EventContainer(attr.Priority, methodInfo, source));
            }
        }

        public void UnregisterEvents(object source)
        {
            foreach (var key in eventMap.Keys) eventMap[key].RemoveWhere(x => x.TargetObject == source);
        }

        public void Invoke(string @event, AsyncEventArgs args)
        {
            pluginManager.InvokeEvent(@event, args);
            if (args is ICancellable {Cancel: true}) return;
            if (args.Handled) return;
            InvokeInternal(@event, args);
        }

        public async Task InvokeAsync(string @event, AsyncEventArgs args)
        {
            await pluginManager.InvokeEventAsync(@event, args);
            if (args is ICancellable {Cancel: true}) return;
            if (args.Handled) return;
            await InvokeInternalAsync(@event, args);
        }
        
        private void InvokeInternal(string @event, AsyncEventArgs args)
        {
            if (!eventMap.TryGetValue(@event, out var events)) return;
            
            foreach (var container in events)
            {
                if (args.Handled) return;
                try
                {
                    container.Method.Invoke(container.TargetObject, new object[] {args});
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error executing event {Event}", @event);
                }
            }
        }

        private async Task InvokeInternalAsync(string @event, AsyncEventArgs args)
        {
            if (!eventMap.TryGetValue(@event, out var events)) return;
            
            foreach (var container in events)
            {
                if (args.Handled) return;
                try
                {
                    if (IsAsync(container.Method))
                    {
                        var task = (Task)container.Method.Invoke(container.TargetObject, new object[] {args});
                        if (task is not null) await task;
                    }
                    else
                        container.Method.Invoke(container.TargetObject, new object[] {args});
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error executing event {Event}", @event);
                }
            }
        }
        
        private static bool IsAsync(MemberInfo methodInfo) => methodInfo.GetCustomAttribute<AsyncStateMachineAttribute>() != null;
    }
}