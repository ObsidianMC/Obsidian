using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using Obsidian.API.Utilities.Interfaces;
using System.Reflection;

namespace Obsidian.Events;
internal readonly struct MinecraftEventDelegateExecutor : IEventExecutor
{
    public required Type EventType { get; init; }

    public required Priority Priority { get; init; }

    public required ILogger? Logger { get; init; }

    public IPluginContainer? PluginContainer { get; init; }

    public required Delegate MethodDelegate { get; init; }

    public ParameterInfo[] GetParameters() => this.MethodDelegate.Method.GetParameters();

    public IEnumerable<TAttribute> GetCustomAttributes<TAttribute>() where TAttribute : Attribute =>
         this.MethodDelegate.Method.GetCustomAttributes<TAttribute>();

    //TODO PARAM INJECTIONS
    public async ValueTask Execute(IServiceProvider serviceProvider, params object[]? methodParams)
    {
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

    public bool MatchParams(object[] args) => throw new NotImplementedException();
}
