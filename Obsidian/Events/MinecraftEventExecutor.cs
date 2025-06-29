using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Obsidian.API.Utilities.Interfaces;
using Obsidian.Plugins;
using System.Reflection;

namespace Obsidian.Events;
internal readonly struct MinecraftEventExecutor : IEventExecutor
{
    public required Type EventType { get; init; }

    public required ILogger Logger { get; init; }

    public required Type ModuleType { get; init; }

    public required PluginContainer? PluginContainer { get; init; }

    public required Priority Priority { get; init; }

    public required ObjectFactory ModuleFactory { get; init; }

    public required ObjectMethodExecutor MethodExecutor { get; init; }

    //TODO PARAM INJECTION
    public async ValueTask Execute(IServiceProvider serviceProvider, params object[]? methodParams)
    {
        var module = this.ModuleFactory!.Invoke(this.PluginContainer?.ServiceScope.ServiceProvider
                ?? serviceProvider, null);//Will inject services through constructor

        this.PluginContainer?.InjectServices(this.Logger, module); //inject through attribute

        if (this.MethodExecutor.MethodReturnType == typeof(ValueTask))
        {
            await (ValueTask)this.MethodExecutor.Execute(module, methodParams)!;
            return;
        }
        else if (this.MethodExecutor.MethodReturnType == typeof(Task))
        {
            await ((Task)this.MethodExecutor.Execute(module, methodParams)!).ConfigureAwait(false);
            return;
        }

        this.MethodExecutor.Execute(module, methodParams);
    }

    public ParameterInfo[] GetParameters() => this.MethodExecutor.MethodParameters;

    public IEnumerable<TAttribute> GetCustomAttributes<TAttribute>() where TAttribute : Attribute =>
        this.MethodExecutor.MethodInfo.GetCustomAttributes<TAttribute>();

    public bool MatchParams(object[] args) => this.GetParameters().Length - 1 == args.Length;
}
