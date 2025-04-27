using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using Obsidian.API.Utilities.Interfaces;
using Obsidian.Events;
using Obsidian.Events.EventArgs;
using Obsidian.Plugins;
using System.Collections.Frozen;
using System.Reflection;

namespace Obsidian.Services;

public sealed class EventDispatcher : IEventDispatcher
{
    private static readonly Type eventPriorityAttributeType = typeof(EventPriorityAttribute);
    private static readonly Type baseMinecraftEventArgsType = typeof(BaseMinecraftEventArgs);
    private static readonly Type minecraftEventHandlerType = typeof(MinecraftEventHandler);

    private readonly ILogger<EventDispatcher> logger;
    private readonly IServiceProvider serviceProvider;
    private readonly FrozenDictionary<string, List<IEventExecutor>> registeredEvents;
    private readonly FrozenDictionary<Type, string> eventNames;

    public EventDispatcher(ILogger<EventDispatcher> logger, IServiceProvider serviceProvider)
    {
        this.logger = logger;
        this.serviceProvider = serviceProvider;
        var events = baseMinecraftEventArgsType.Assembly.GetTypes()
            .Where(x => x.IsSubclassOf(baseMinecraftEventArgsType) && !x.IsAbstract)
            .ToList();

        //Temp workaround until we decide to expose this event
        var dict = new Dictionary<string, List<IEventExecutor>>()
        {
            { "QueuePacket", [] }
        };

        var names = new Dictionary<Type, string>()
        {
            {typeof(QueuePacketEventArgs), "QueuePacket" }
        };

        foreach (var eventType in events)
        {

            var name = eventType.Name.TrimEventArgs();

            dict.Add(name, []);
            names.Add(eventType, name);
        }

        this.registeredEvents = dict.ToFrozenDictionary();
        this.eventNames = names.ToFrozenDictionary();
    }

    public void RegisterEvents<TEventModule>(PluginContainer? pluginContainer) where TEventModule : MinecraftEventHandler
    {
        var eventModule = typeof(TEventModule);
        var eventModuleTypeInfo = eventModule.GetTypeInfo();
        var methods = eventModule.GetMethods().Where(x => x.CustomAttributes.Any(x => x.AttributeType == eventPriorityAttributeType));

        foreach (var method in methods)
        {
            var eventPriorityAttribute = method.GetCustomAttribute<EventPriorityAttribute>()!;
            var eventType = method.GetParameters().FirstOrDefault()?.ParameterType ?? throw new InvalidOperationException("Method must contain a BaseMinecraftEventArgs type as the first parameter.");

            if (!eventType.IsSubclassOf(baseMinecraftEventArgsType))
                throw new InvalidOperationException("Method must contain a BaseMinecraftEventArgs type as the first parameter.");

            if (!this.registeredEvents.TryGetValue(this.eventNames[eventType], out var values))
                continue;

            values.Add(new MinecraftEventExecutor
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

    public void RegisterEvent<TEventArgs>(PluginContainer pluginContainer, ValueTaskContextDelegate<TEventArgs> contextDelegate,
        Priority priority = Priority.Low)
        where TEventArgs : BaseMinecraftEventArgs
    {
        if (!this.registeredEvents.TryGetValue(this.eventNames[typeof(TEventArgs)], out var values))
            return;

        values.Add(new MinecraftEventDelegateExecutor
        {
            EventType = typeof(TEventArgs),
            PluginContainer = pluginContainer,
            Priority = priority,
            MethodDelegate = contextDelegate,
            Logger = this.logger
        });
    }

    public void RegisterEvent(PluginContainer? pluginContainer, Delegate handler, Priority priority = Priority.Low)
    {
        var eventType = handler.Method.GetParameters().FirstOrDefault()?.ParameterType ??
            throw new InvalidOperationException("Missing parameter for event.");

        if (!this.registeredEvents.TryGetValue(this.eventNames[eventType], out var values))
            return;

        values.Add(new MinecraftEventDelegateExecutor
        {
            EventType = eventType,
            PluginContainer = pluginContainer,
            Priority = priority,
            MethodDelegate = handler,
            Logger = this.logger
        });
    }

    public void RegisterEvents(PluginContainer? pluginContainer = null)
    {
        var assembly = pluginContainer?.PluginAssembly ?? Assembly.GetExecutingAssembly();


        var modules = assembly.GetTypes().Where(x => x.IsSubclassOf(minecraftEventHandlerType));

        this.RegisterEventsInternal(modules, pluginContainer);
    }

    private void RegisterEventsInternal(IEnumerable<Type>? modules, PluginContainer? pluginContainer = null)
    {
        if (modules == null)
            return;

        foreach (var eventModule in modules)
        {
            var eventModuleTypeInfo = eventModule.GetTypeInfo();
            var methods = eventModule.GetMethods().Where(x => x.CustomAttributes.Any(x => x.AttributeType == eventPriorityAttributeType));

            foreach (var method in methods)
            {
                var eventPriorityAttribute = method.GetCustomAttribute<EventPriorityAttribute>()!;
                var eventType = method.GetParameters().FirstOrDefault()?.ParameterType ?? throw new InvalidOperationException("Method must contain a BaseMinecraftEventArgs type as the first parameter.");

                if (!eventType.IsSubclassOf(baseMinecraftEventArgsType))
                    throw new InvalidOperationException("Method must contain a BaseMinecraftEventArgs type as the first parameter.");

                if (!this.registeredEvents.TryGetValue(this.eventNames[eventType], out var values))
                    continue;

                values.Add(new MinecraftEventExecutor
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

    public async ValueTask<EventResult> ExecuteEventAsync<TEventArgs>(TEventArgs eventArgs) where TEventArgs : BaseMinecraftEventArgs
    {
        var eventType = eventArgs.GetType();
        using var serviceScope = this.serviceProvider.CreateScope();

        if (!this.registeredEvents.TryGetValue(this.eventNames[typeof(TEventArgs)], out var events))
            return EventResult.Completed;

        var foundEvents = events.OrderBy(x => x.Priority);//Plugins with the lowest priority must be called first 

        var eventResult = EventResult.Completed;

        foreach (var @event in foundEvents)
        {
            try
            {
                await @event.Execute(serviceScope.ServiceProvider, [eventArgs]);

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
