using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Events;
using Obsidian.Plugins;
using System.Reflection;

namespace Obsidian.Services;

//TODO BETTER NAME MAYBE??
public sealed class EventDispatcher(ILogger<EventDispatcher> logger) : IDisposable
{
    private static Type eventPriorityAttributeType = typeof(EventPriorityAttribute);
    private static Type baseMinecraftEventArgsType = typeof(BaseMinecraftEventArgs);

    private readonly List<MinecraftEvent> registeredEvents = [];
    private readonly ILogger<EventDispatcher> logger = logger;

    public void RegisterEvents<TEventModule>(PluginContainer pluginContainer) where TEventModule : MinecraftEventHandler
    {
        var eventModule = typeof(TEventModule);

        var methods = eventModule.GetMethods().Where(x => x.CustomAttributes.Any(x => x.AttributeType == eventPriorityAttributeType));

        foreach (var method in methods)
        {
            var eventPriorityAttribute = method.GetCustomAttribute<EventPriorityAttribute>()!;
            var eventType = method.GetParameters().FirstOrDefault()?.ParameterType ?? throw new InvalidOperationException("Method must contain a BaseMinecraftEventArgs type as the first parameter.");

            if (!eventType.IsAssignableFrom(baseMinecraftEventArgsType))
                throw new InvalidOperationException("Method must contain a BaseMinecraftEventArgs type as the first parameter.");

            this.registeredEvents.Add(new()
            {
                EventType = eventType,
                ModuleFactory = ActivatorUtilities.CreateFactory(eventModule, []),
                PluginContainer = pluginContainer,
                Priority = eventPriorityAttribute.Priority,
                MethodExecutor = ObjectMethodExecutor.Create(method, eventModule.GetTypeInfo()),
                ModuleType = eventModule
            });
        }
    }

    public async ValueTask<EventResult> ExecuteEventAsync<TEventArgs>(TEventArgs eventArgs) where TEventArgs : BaseMinecraftEventArgs
    {
        var events = this.registeredEvents.Where(x => x.EventType == eventArgs.GetType())
            .OrderBy(x => x.Priority);//Plugins with the lowest priority must be called first 

        var eventResult = EventResult.Completed;

        foreach (var @event in events)
        {
            var module = @event.ModuleFactory.Invoke(@event.PluginContainer.ServiceScope.ServiceProvider, null)//Will inject services through constructor
                    ?? throw new InvalidOperationException("Failed to initialize module from factory.");

            //inject through attribute
            @event.PluginContainer.InjectServices(this.logger, module);

            try
            {
                var returnResult = @event.MethodExecutor.Execute(module, new[] { eventArgs });//Maybe have method param service injection??

                if (returnResult is ValueTask valueTask)
                    await valueTask;
                else if (returnResult is Task task)
                    await task.ConfigureAwait(false);

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
        this.registeredEvents.Clear();
    }
}

public enum EventResult
{
    Cancelled,

    Completed,

    Failed
}
