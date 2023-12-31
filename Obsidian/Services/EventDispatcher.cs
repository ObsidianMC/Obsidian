using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using Obsidian.Events;
using Obsidian.Plugins;
using System.Collections.Frozen;
using System.Reflection;

namespace Obsidian.Services;

//TODO BETTER NAME MAYBE??
public sealed class EventDispatcher : IDisposable
{
    private static readonly Type eventPriorityAttributeType = typeof(EventPriorityAttribute);
    private static readonly Type baseMinecraftEventArgsType = typeof(BaseMinecraftEventArgs);
    private static readonly Type minecraftEventHandlerType = typeof(MinecraftEventHandler);

    private readonly ILogger<EventDispatcher> logger;

    private readonly FrozenDictionary<string, List<MinecraftEvent>> registeredEvents;

    public EventDispatcher(ILogger<EventDispatcher> logger)
    {
        this.logger = logger;

        var events = typeof(INamedEvent).Assembly.GetTypes().Where(x => x.IsAssignableFrom(baseMinecraftEventArgsType));
        var dict = new Dictionary<string, List<MinecraftEvent>>();
        foreach (var eventType in events)
        {
            var eventName = eventType.GetProperty("Name")?.GetValue(null)?.ToString() ?? throw new NullReferenceException();

            dict.Add(eventName, []);
        }

        this.registeredEvents = dict.ToFrozenDictionary();
    }

    public void RegisterEvents<TEventModule>(PluginContainer pluginContainer) where TEventModule : MinecraftEventHandler
    {
        var eventModule = typeof(TEventModule);
        var eventModuleTypeInfo = eventModule.GetTypeInfo();
        var methods = eventModule.GetMethods().Where(x => x.CustomAttributes.Any(x => x.AttributeType == eventPriorityAttributeType));

        foreach (var method in methods)
        {
            var eventPriorityAttribute = method.GetCustomAttribute<EventPriorityAttribute>()!;
            var eventType = method.GetParameters().FirstOrDefault()?.ParameterType ?? throw new InvalidOperationException("Method must contain a BaseMinecraftEventArgs type as the first parameter.");
            var eventName = eventType.GetProperty("Name")?.GetValue(null)?.ToString() ?? throw new NullReferenceException();

            if (!eventType.IsAssignableFrom(baseMinecraftEventArgsType))
                throw new InvalidOperationException("Method must contain a BaseMinecraftEventArgs type as the first parameter.");

            if (!this.registeredEvents.TryGetValue(eventName, out var values))
                continue;

            values.Add(new()
            {
                EventType = eventType,
                ModuleFactory = ActivatorUtilities.CreateFactory(eventModule, []),
                PluginContainer = pluginContainer,
                Priority = eventPriorityAttribute.Priority,
                MethodExecutor = ObjectMethodExecutor.Create(method, eventModuleTypeInfo),
                ModuleType = eventModule,
                Logger = this.logger
            });
        }
    }

    public void RegisterEvent<TEventArgs>(PluginContainer pluginContainer, ContextDelegate<TEventArgs> contextDelegate, Priority priority = Priority.Low) where TEventArgs : BaseMinecraftEventArgs, INamedEvent
    {
        if (!this.registeredEvents.TryGetValue(TEventArgs.Name, out var values))
            return;

        values.Add(new()
        {
            EventType = typeof(TEventArgs),
            PluginContainer = pluginContainer,
            Priority = priority,
            MethodDelegate = contextDelegate,
            Logger = this.logger
        });

    }

    public void RegisterEvents(PluginContainer pluginContainer)
    {
        var modules = pluginContainer.PluginAssembly.GetTypes().Where(x => x.IsAssignableFrom(minecraftEventHandlerType));

        foreach (var eventModule in modules)
        {
            var eventModuleTypeInfo = eventModule.GetTypeInfo();
            var methods = eventModule.GetMethods().Where(x => x.CustomAttributes.Any(x => x.AttributeType == eventPriorityAttributeType));

            foreach (var method in methods)
            {
                var eventPriorityAttribute = method.GetCustomAttribute<EventPriorityAttribute>()!;
                var eventType = method.GetParameters().FirstOrDefault()?.ParameterType ?? throw new InvalidOperationException("Method must contain a BaseMinecraftEventArgs type as the first parameter.");
                var eventName = eventType.GetProperty("Name")?.GetValue(null)?.ToString() ?? throw new NullReferenceException();

                if (!eventType.IsAssignableFrom(baseMinecraftEventArgsType))
                    throw new InvalidOperationException("Method must contain a BaseMinecraftEventArgs type as the first parameter.");

                if (!this.registeredEvents.TryGetValue(eventName, out var values))
                    continue;

                values.Add(new()
                {
                    EventType = eventType,
                    ModuleFactory = ActivatorUtilities.CreateFactory(eventModule, []),
                    PluginContainer = pluginContainer,
                    Priority = eventPriorityAttribute.Priority,
                    MethodExecutor = ObjectMethodExecutor.Create(method, eventModuleTypeInfo),
                    ModuleType = eventModule,
                    Logger = this.logger
                });
            }
        }
    }

    public async ValueTask<EventResult> ExecuteEventAsync<TEventArgs>(TEventArgs eventArgs) where TEventArgs : BaseMinecraftEventArgs, INamedEvent
    {
        var eventType = eventArgs.GetType();

        if (!this.registeredEvents.TryGetValue(TEventArgs.Name, out var events))
            return EventResult.Completed;

        var foundEvents = events.OrderBy(x => x.Priority);//Plugins with the lowest priority must be called first 

        var eventResult = EventResult.Completed;

        foreach (var @event in foundEvents)
        {
            try
            {
                await @event.Execute(new[] { eventArgs });

                if (eventArgs is ICancellable cancellable && cancellable.IsCancelled)
                    eventResult = EventResult.Cancelled;
            }
            catch (OperationCanceledException) { }//IGNORE this exception 
            catch (Exception ex)
            {
                this.logger.LogCritical(ex, "failed to execute event.");

                return EventResult.Failed;
            }
        }

        return eventResult;
    }

    public void Dispose()
    {
        foreach (var (_, values) in this.registeredEvents)
        {
            values.Clear();
        }
    }
}

public enum EventResult
{
    Cancelled,

    Completed,

    Failed
}
