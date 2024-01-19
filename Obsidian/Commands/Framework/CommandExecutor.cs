using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Obsidian.Plugins;
using System.Diagnostics;
using System.Reflection;

namespace Obsidian.Commands.Framework;
public sealed class CommandExecutor
{
    public bool HasModule => this.ModuleType != null;

    public required ILogger? Logger { get; init; }

    public PluginContainer? PluginContainer { get; init; }

    public Type? ModuleType { get; init; }

    public ObjectFactory? ModuleFactory { get; init; }

    internal ObjectMethodExecutor? MethodExecutor { get; init; }

    public Delegate? MethodDelegate { get; init; }

    public ParameterInfo[] GetParameters()
    {
        if (this.MethodDelegate != null)
            return this.MethodDelegate.Method.GetParameters();

        if (this.MethodExecutor == null)
            throw new UnreachableException("Method executor is null");

        return this.MethodExecutor!.MethodParameters;
    }

    public IEnumerable<TAttribute> GetCustomAttributes<TAttribute>() where TAttribute : Attribute
    {
        if (this.MethodDelegate != null)
            return this.MethodDelegate.Method.GetCustomAttributes<TAttribute>();

        if (this.MethodExecutor == null)
            throw new UnreachableException("Method executor is null");

        return this.MethodExecutor.MethodInfo.GetCustomAttributes<TAttribute>();
    }

    public async ValueTask Execute(IServiceProvider serviceProvider, CommandContext context, params object[]? methodParams)
    {
        if (this.MethodExecutor != null)
        {
            object? module = this.PluginContainer != null
                ? this.HasModule ? CommandModuleFactory.CreateModule(this.ModuleFactory!, context, this.PluginContainer) : null
                : this.HasModule ? CommandModuleFactory.CreateModule(this.ModuleFactory!, context, serviceProvider) : null;

            if (module == null)
                throw new InvalidOperationException("Failed to create module class.");

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

    public bool MatchParams(string[] args) =>
        this.HasModule ? this.GetParameters().Length == args.Length : this.GetParameters().Length - 1 == args.Length;
}
