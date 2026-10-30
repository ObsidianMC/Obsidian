using Microsoft.Extensions.Logging;
using Obsidian.API.Utilities.Interfaces;
using Obsidian.Plugins;
using System.Reflection;

namespace Obsidian.Commands.Framework;
internal sealed class CommandDelegateExecutor : IExecutor<CommandContext>
{
    public required ILogger? Logger { get; init; }

    public PluginContainer? PluginContainer { get; init; }

    public required Delegate MethodDelegate { get; init; }

    public ParameterInfo[] GetParameters() => this.MethodDelegate.Method.GetParameters()[1..];

    public IEnumerable<TAttribute> GetCustomAttributes<TAttribute>() where TAttribute : Attribute =>
         this.MethodDelegate.Method.GetCustomAttributes<TAttribute>();

    public async ValueTask Execute(IServiceProvider serviceProvider, CommandContext context, params object[]? methodParams)
    {
        object[] args = [context, .. methodParams];

        context.SetResult(true, 1);

        if (this.MethodDelegate.Method.ReturnType == typeof(Task<int>))
        {
            var result = await (Task<int>)this.MethodDelegate.DynamicInvoke(args)!;
            context.SetResult(context.Succeeded, result);
            return;
        }

        if (this.MethodDelegate.Method.ReturnType == typeof(ValueTask<int>))
        {
            var result = await (ValueTask<int>)this.MethodDelegate.DynamicInvoke(args)!;
            context.SetResult(context.Succeeded, result);
            return;
        }

        if (this.MethodDelegate.Method.ReturnType == typeof(int))
        {
            var result = (int)this.MethodDelegate.DynamicInvoke(args)!;
            context.SetResult(context.Succeeded, result);
            return;
        }

        if (this.MethodDelegate.Method.ReturnType == typeof(ValueTask))
        {
            await (ValueTask)this.MethodDelegate.DynamicInvoke(args)!;
            return;
        }
        else if (this.MethodDelegate.Method.ReturnType == typeof(Task))
        {
            await ((Task)this.MethodDelegate.DynamicInvoke(args)!).ConfigureAwait(false);
            return;
        }

        this.MethodDelegate.DynamicInvoke(args);
    }

    public bool MatchParams(object[] args) => this.GetParameters().Length == args.Length;
}
