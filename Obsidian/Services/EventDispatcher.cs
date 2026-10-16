using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.API.Plugins;
using Obsidian.API.Utilities.Interfaces;
using Obsidian.Events;
using Obsidian.Events.EventArgs;
using Obsidian.Plugins;
using Obsidian.Plugins.PluginProviders;
using System.Collections.Frozen;
using System.Reflection;

namespace Obsidian.Services;

public sealed partial class EventDispatcher : IEventDispatcher
{
    private static readonly Type eventPriorityAttributeType = typeof(EventPriorityAttribute);
    private static readonly Type baseMinecraftEventArgsType = typeof(BaseMinecraftEventArgs);
    private static readonly Type minecraftEventHandlerType = typeof(MinecraftEventHandler);

    private readonly ILogger<EventDispatcher> logger;
    private readonly IServiceProvider serviceProvider;
    // Each event's handlers, sorted by priority. An immutable array, replaced whole, lets plugins (un)register while
    // events run on other threads, which happens when a plugin reloads.
    private readonly FrozenDictionary<string, EventHandlers> registeredEvents;
    private readonly FrozenDictionary<Type, string> eventNames;

    public EventDispatcher(ILogger<EventDispatcher> logger, IServiceProvider serviceProvider)
    {
        this.logger = logger;
        this.serviceProvider = serviceProvider;
        var events = baseMinecraftEventArgsType.Assembly.GetTypes()
            .Where(x => x.IsSubclassOf(baseMinecraftEventArgsType) && !x.IsAbstract)
            .ToList();

        //Temp workaround until we decide to expose this event
        var dict = new Dictionary<string, EventHandlers>()
        {
            { "QueuePacket", new() }
        };

        var names = new Dictionary<Type, string>()
        {
            {typeof(QueuePacketEventArgs), "QueuePacket" }
        };

        foreach (var eventType in events)
        {

            var name = eventType.Name.TrimEventArgs();

            dict.Add(name, new());
            names.Add(eventType, name);
        }

        this.registeredEvents = dict.ToFrozenDictionary();
        this.eventNames = names.ToFrozenDictionary();
    }

    public void RegisterEvents<TEventModule>(IPluginContainer? pluginContainer)
        where TEventModule : MinecraftEventHandler =>
        this.RegisterEventsInternal([typeof(TEventModule)], pluginContainer);

    public void RegisterEvent<TEventArgs>(IPluginContainer pluginContainer, ValueTaskContextDelegate<TEventArgs> contextDelegate,
        Priority priority = Priority.Low)
        where TEventArgs : BaseMinecraftEventArgs
    {
        this.Add(typeof(TEventArgs), pluginContainer, new MinecraftEventDelegateExecutor
        {
            EventType = typeof(TEventArgs),
            PluginContainer = pluginContainer,
            Priority = priority,
            MethodDelegate = contextDelegate,
            Logger = this.logger
        });
    }

    public void RegisterEvent(IPluginContainer? pluginContainer, Delegate handler, Priority priority = Priority.Low)
    {
        // A plugin's handler is the plugin's, so unloading it removes the handler, even when no container is given.
        pluginContainer ??= PluginLoadContext.OwnerOf(handler.Method.Module.Assembly);

        var eventType = handler.Method.GetParameters().FirstOrDefault()?.ParameterType ??
            throw new InvalidOperationException("Missing parameter for event.");

        this.Add(eventType, pluginContainer, new MinecraftEventDelegateExecutor
        {
            EventType = eventType,
            PluginContainer = pluginContainer,
            Priority = priority,
            MethodDelegate = handler,
            Logger = this.logger
        });
    }

    public void RegisterEvents(IPluginContainer? pluginContainer = null)
    {
        var assembly = pluginContainer?.PluginAssembly ?? Assembly.GetExecutingAssembly();

        var modules = assembly.GetTypes().Where(x => x.IsSubclassOf(minecraftEventHandlerType));

        this.RegisterEventsInternal(modules, pluginContainer);
    }

    private void RegisterEventsInternal(IEnumerable<Type>? modules, IPluginContainer? pluginContainer = null)
    {
        if (modules is null)
            return;

        foreach (var eventModule in modules)
        {
            var owner = pluginContainer ?? PluginLoadContext.OwnerOf(eventModule.Assembly);
            var eventModuleTypeInfo = eventModule.GetTypeInfo();
            var methods = eventModule.GetMethods().Where(x => x.CustomAttributes.Any(x => x.AttributeType == eventPriorityAttributeType));

            foreach (var method in methods)
            {
                var eventPriorityAttribute = method.GetCustomAttribute<EventPriorityAttribute>()!;
                var eventType = method.GetParameters().FirstOrDefault()?.ParameterType
                ?? throw new InvalidOperationException("Method must contain a BaseMinecraftEventArgs type as the first parameter.");

                if (!eventType.IsSubclassOf(baseMinecraftEventArgsType))
                    throw new InvalidOperationException("Method must contain a BaseMinecraftEventArgs type as the first parameter.");

                this.Add(eventType, owner, new MinecraftEventExecutor
                {
                    EventType = eventType,
                    ModuleFactory = ActivatorUtilities.CreateFactory(eventModule, []),
                    PluginContainer = owner,
                    Priority = eventPriorityAttribute.Priority,
                    MethodExecutor = ObjectMethodExecutor.Create(method, eventModuleTypeInfo),
                    ModuleType = eventModule,
                    Logger = this.logger
                });
            }
        }
    }

    public ValueTask<EventResult> ExecuteEventAsync<TEventArgs>(TEventArgs eventArgs)
        where TEventArgs : BaseMinecraftEventArgs =>
        this.ExecuteEventAsync(eventArgs, unloading: null);

    /// <summary>
    /// Runs the handlers of an event that <paramref name="unloading"/>'s unloading raises, like closing its menus. Its
    /// handlers run too, although it takes no other calls.
    /// </summary>
    internal async ValueTask<EventResult> ExecuteEventAsync<TEventArgs>(
        TEventArgs eventArgs,
        PluginContainer? unloading)
        where TEventArgs : BaseMinecraftEventArgs
    {
        var eventType = eventArgs.GetType();
        using var serviceScope = this.serviceProvider.CreateScope();

        if (!this.registeredEvents.TryGetValue(this.eventNames[typeof(TEventArgs)], out var events))
            return EventResult.Completed;

        var eventResult = EventResult.Completed;

        // Handlers with the lowest priority run first.
        foreach (var registration in events.Registrations)
        {
            // The snapshot can hold handlers of a plugin that started unloading; those are skipped, and releasing the
            // plugin waits for the ones that already run. The call also gives the handler its plugin's own server in
            // the arguments (e.Server), and the server's handlers the server's, without changing the shared arguments.
            var plugin = registration.Plugin as PluginContainer;
            using var call = plugin is null
                ? PluginCall.EnterHost()
                : plugin == unloading
                    ? plugin.EnterUnloading()
                    : plugin.TryEnter();

            if (plugin is not null && call is null)
                continue;

            try
            {
                await registration.Executor.Execute(serviceScope.ServiceProvider, [eventArgs]);

                if (eventArgs is ICancellable cancellable && cancellable.IsCancelled)
                    eventResult = EventResult.Cancelled;
            }
            catch (OperationCanceledException) { }//IGNORE this exception 
            catch (Exception ex)
            {
                Log.HandlerFailed(this.logger, ex, eventType.Name);

                return EventResult.Failed;
            }
        }

        return eventResult;
    }

    /// <summary>
    /// Removes every event handler a plugin registered, as the plugin unloads.
    /// </summary>
    public void UnregisterPluginEvents(IPluginContainer plugin)
    {
        foreach (var (_, handlers) in this.registeredEvents)
        {
            ImmutableInterlocked.Update(ref handlers.Registrations,
                registrations => registrations.RemoveAll(registration => registration.Plugin == plugin));
        }
    }

    public void Dispose()
    {
        foreach (var (_, handlers) in this.registeredEvents)
            handlers.Registrations = [];
    }

    // Refuses an unloading plugin's handler: unloading closes the plugin before removing its handlers, so one added
    // meanwhile is removed either then or here.
    private void Add(Type eventType, IPluginContainer? plugin, IEventExecutor executor)
    {
        if (!this.registeredEvents.TryGetValue(this.eventNames[eventType], out var handlers))
            return;

        var owner = plugin as PluginContainer;
        owner?.ThrowIfClosing();

        var registration = new EventRegistration(executor, plugin);

        // Inserted after the handlers of the same priority, so handlers of one priority run in registration order.
        ImmutableInterlocked.Update(ref handlers.Registrations, registrations =>
        {
            var index = 0;
            while (index < registrations.Length && registrations[index].Executor.Priority <= executor.Priority)
                index++;

            return registrations.Insert(index, registration);
        });

        if (owner is not { IsClosing: true })
            return;

        ImmutableInterlocked.Update(
            ref handlers.Registrations,
            registrations => registrations.Remove(registration, ReferenceEqualityComparer.Instance));
        owner.ThrowIfClosing();
    }

    private sealed class EventHandlers
    {
        public ImmutableArray<EventRegistration> Registrations = [];
    }

    // A class, so that a registration that raced its plugin's unloading is removed by reference, alone.
    private sealed record EventRegistration(IEventExecutor Executor, IPluginContainer? Plugin);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "An event handler for {EventName} failed")]
        public static partial void HandlerFailed(ILogger logger, Exception exception, string eventName);
    }
}
