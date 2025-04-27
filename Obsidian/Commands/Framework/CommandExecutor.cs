using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using Obsidian.API.Utilities.Interfaces;
using System.Reflection;

namespace Obsidian.Commands.Framework;
internal sealed class CommandExecutor : IExecutor<CommandContext>
{
    public required ILogger? Logger { get; init; }

    public IPluginContainer? PluginContainer { get; init; }

    public required Type ModuleType { get; init; }

    public required ObjectFactory ModuleFactory { get; init; }

    public required ObjectMethodExecutor MethodExecutor { get; init; }

    public ParameterInfo[] GetParameters() => this.MethodExecutor.MethodParameters;

    public IEnumerable<TAttribute> GetCustomAttributes<TAttribute>() where TAttribute : Attribute =>
        this.MethodExecutor.MethodInfo.GetCustomAttributes<TAttribute>();

    public async ValueTask Execute(IServiceProvider serviceProvider, CommandContext context, params object[]? methodParams)
    {
        var module = CommandModuleFactory.CreateModule(this.ModuleFactory, context,
                this.PluginContainer?.ServiceScope.ServiceProvider ?? serviceProvider);

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

    public bool MatchParams(object[] args) => this.GetParameters().Length == args.Length;
}
