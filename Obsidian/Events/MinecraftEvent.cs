using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Plugins;
using System.Diagnostics;

namespace Obsidian.Events;
internal readonly struct MinecraftEvent
{
    public required Type EventType { get; init; }

    public required ILogger Logger { get; init; }

    public Type? ModuleType { get; init; }

    public required PluginContainer PluginContainer { get; init; }

    public required Priority Priority { get; init; }

    public ObjectFactory? ModuleFactory { get; init; }

    public ObjectMethodExecutor? MethodExecutor { get; init; }

    public Delegate? MethodDelegate { get; init; }

    //TODO PARAM INJECTION
    public async ValueTask Execute(params object[]? methodParams)
    {
        if (this.MethodExecutor != null)
        {
            var module = this.ModuleFactory!.Invoke(this.PluginContainer.ServiceScope.ServiceProvider, null)//Will inject services through constructor
            ?? throw new InvalidOperationException("Failed to initialize module from factory.");

            this.PluginContainer.InjectServices(this.Logger, module); //inject through attribute

            if (this.MethodExecutor.MethodReturnType == typeof(ValueTask))
            {
                await (ValueTask)this.MethodExecutor.Execute(module!, methodParams)!;
                return;
            }
            else if (this.MethodExecutor.MethodReturnType == typeof(Task))
            {
                await ((Task)this.MethodExecutor.Execute(module, methodParams)!).ConfigureAwait(false);
                return;
            }

            this.MethodExecutor.Execute(module, methodParams);

            return;
        }

        if (this.MethodDelegate == null)
            throw new UnreachableException();

        if (this.MethodDelegate.Method.ReturnType == typeof(ValueTask))
        {
            await (ValueTask)this.MethodDelegate.DynamicInvoke(methodParams)!;
            return;
        }
        else if (this.MethodDelegate.Method.ReturnType == typeof(Task))
        {
            await ((Task)this.MethodDelegate.DynamicInvoke(methodParams)!).ConfigureAwait(false);
            return;
        }

        this.MethodDelegate.DynamicInvoke(methodParams);
    }
}
