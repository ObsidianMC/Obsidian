using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API.Utilities;
using Obsidian.Commands.Framework.Exceptions;
using Obsidian.Plugins;
using Obsidian.Utilities.Interfaces;
using System.Reflection;

namespace Obsidian.Commands.Framework.Entities;

public sealed class Command
{
    internal CommandIssuers AllowedIssuers { get; init; }

    private ILogger? Logger => this.CommandHandler.logger;

    public required CommandHandler CommandHandler { get; init; }
    public required PluginContainer? PluginContainer { get; init; }
    public required string Name { get; init; }

    public string[] Aliases { get; init; } = [];
    public string? Description { get; init; }
    public string? Usage { get; init; }

    public List<IExecutor<CommandContext>> Overloads { get; init; } = [];
    public BaseExecutionCheckAttribute[] ExecutionChecks { get; init; } = [];

    public Command? Parent { get; init; }

    internal Command() { }

    public bool CheckCommand(string[] input, Command? parent)
    {
        return Parent == parent && input.Length > 0 && (Name == input[0] || Aliases.Contains(input[0]));
    }

    /// <summary>
    /// Gets the full qualified command name.
    /// </summary>
    /// <returns>Full qualified command name.</returns>
    public string GetQualifiedName()
    {
        var c = this;
        string name = c.Name;

        while (c.Parent != null)
        {
            name = $"{c.Parent.Name} {name}";
            c = c.Parent;
        }

        return name;
    }

    /// <summary>
    /// Executes this command.
    /// </summary>
    /// <typeparam name="T">Context type.</typeparam>
    /// <param name="Context">Execution context.</param>
    /// <returns></returns>
    public async Task ExecuteAsync(CommandContext context, string[] args)
    {
        // Check whether the issuer can execute this command
        if (!AllowedIssuers.HasFlag(context.Sender.Issuer))
        {
            throw new DisallowedCommandIssuerException(
                $"Command {GetQualifiedName()} cannot be executed as {context.Sender.Issuer}", AllowedIssuers);
        }

        var executors = Overloads.Where(x => x.MatchParams(args)
            || x.GetParameters().LastOrDefault()?.GetCustomAttribute<RemainingAttribute>() != null);

        // Find matching overload
        if (executors == null)
        {
            //TODO since commands can have multiple usages, if this is empty we should print out all of the args for usage
            await context.Sender.SendMessageAsync($"&4Correct usage: {Usage}");

            return;
        }
        
        IExecutor<CommandContext> executor = default!;

        var success = false;
        foreach (var exec in executors)
        {
            executor = exec;

            var methodParams = exec.GetParameters();
            for (int i = 0; i < args.Length; i++)
            {
                var param = methodParams[i];
                var arg = args[i];

                if (!CommandHandler.IsValidArgumentType(param.ParameterType))
                {
                    success = false;
                    break;
                }

                var parser = CommandHandler.GetArgumentParser(param.ParameterType);
                if (parser.TryParseArgument(arg, context, out _))
                {
                    success = true;
                    continue;
                }

                success = false;
                break;
            }

            if (success)
                break;
        }

        if (!success)
            throw new InvalidOperationException($"Failed to find valid executor for /{this.Name}");

        await this.ExecuteAsync(executor, context, args);
    }

    private async Task ExecuteAsync(IExecutor<CommandContext> commandExecutor, CommandContext context, string[] args)
    {
        using var serviceScope = this.CommandHandler.ServiceProvider.CreateScope();

        var methodparams = commandExecutor.GetParameters().ToArray();
        //commandExecutor.GetParameters().Skip(1).ToArray();

        var parsedargs = new object[args.Length];

        for (int i = 0; i < args.Length; i++)
        {
            // Current param and arg
            var paraminfo = methodparams[i];

            var arg = args[i];

            // This can only be true if we get a [Remaining] arg. Sets arg to remaining text.
            if (args.Length > methodparams.Length && i == methodparams.Length - 1)
            {
                arg = string.Join(' ', args.Skip(i));
            }

            // Checks if there is any valid registered command handler
            if (CommandHandler.IsValidArgumentType(paraminfo.ParameterType))
            {
                var parser = CommandHandler.GetArgumentParser(paraminfo.ParameterType);

                // cast with reflection?
                if (parser.TryParseArgument(arg, context, out var parserResult))
                {
                    // parse success!
                    parsedargs[i] = parserResult;
                }
                else
                {
                    // Argument can't be parsed to the parser's type.
                    throw new CommandArgumentParsingException($"Argument '{arg}' was not parseable to {paraminfo.ParameterType.Name}!");
                }
            }
            else
            {
                throw new NoSuchParserException($"No valid argumentparser found for type {paraminfo.ParameterType.Name}!");
            }
        }

        // do execution checks
        var checks = commandExecutor.GetCustomAttributes<BaseExecutionCheckAttribute>();

        foreach (var c in checks)
        {
            if (!await c.RunChecksAsync(context))
            {
                // A check failed.
                // TODO: Tell user what arg failed?
                throw c switch
                {
                    RequirePermissionAttribute r => new NoPermissionException(r.RequiredPermissions, r.CheckType),
                    _ => new CommandExecutionCheckException($"One or more execution checks failed."),
                };
            }
        }

        // await the command with it's args
        await commandExecutor.Execute(serviceScope.ServiceProvider, context, parsedargs);
    }

    public override string ToString() => $"{CommandHelpers.DefaultPrefix}{GetQualifiedName()}";
}
