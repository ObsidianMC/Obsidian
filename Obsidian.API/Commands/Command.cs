using Microsoft.Extensions.DependencyInjection;
using Obsidian.API.Commands.Exceptions;
using Obsidian.API.Plugins;
using Obsidian.API.Utilities;
using Obsidian.API.Utilities.Interfaces;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Obsidian.API.Commands;
public sealed class Command
{
    public CommandIssuers AllowedIssuers { get; init; }

    public required ICommandHandler CommandHandler { get; init; }
    public required IPluginContainer? PluginContainer { get; init; }
    public required string Name { get; init; }

    public ImmutableArray<string> Aliases { get; init; } = [];
    public string? Description { get; init; }
    public string? Usage { get; init; }

    public List<IExecutor<CommandContext>> Overloads { get; init; } = [];
    public ImmutableArray<BaseExecutionCheckAttribute> ExecutionChecks { get; init; } = [];

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
    /// <param name="context">Execution context.</param>
    /// <param name="args">The arguments the command was issued with.</param>
    public async Task ExecuteAsync(CommandContext context, string[] args)
    {
        // Check whether the issuer can execute this command and the groups it belongs to
        if (SelfAndGroups().FirstOrDefault(command => !command.AllowedIssuers.HasFlag(context.Sender.Issuer)) is Command restricted)
        {
            throw new DisallowedCommandIssuerException(
                $"Command {GetQualifiedName()} cannot be executed as {context.Sender.Issuer}", restricted.AllowedIssuers);
        }

        var executors = Overloads.Where(x => x.MatchParams(args)
            || x.GetParameters().LastOrDefault()?.GetCustomAttribute<RemainingAttribute>() != null);

        // Find matching overload
        if (executors == null)
        {
            //TODO since commands can have multiple usages, if this is empty we should print out all of the args for usage
            //throw new InvalidCommandOverloadException($"No such overload for command {this.GetQualifiedName()}");
            await context.Sender.SendMessageAsync(ChatMessage.Simple($"Correct usage: {Usage}", ChatColor.Red));

            return;
        }

        if (!this.TryFindExecutor(executors, args, context, out var executor, out var boundArgs))
        {
            await context.Sender.SendMessageAsync(ChatMessage.Simple($"Correct usage: {Usage}", ChatColor.Red));
            return;
        }

        await this.ExecuteAsync(executor, context, boundArgs);
    }

    /// <summary>
    /// Finds the first overload whose parameters accept every argument.
    /// </summary>
    /// <param name="executors">The overloads to try, in order.</param>
    /// <param name="args">The arguments the command was issued with.</param>
    /// <param name="context">Execution context.</param>
    /// <param name="executor">The first overload that accepts <paramref name="args"/>.</param>
    /// <param name="boundArgs">The arguments bound to the executor's parameters, one per parameter.</param>
    private bool TryFindExecutor(IEnumerable<IExecutor<CommandContext>> executors, string[] args, CommandContext context,
        [NotNullWhen(true)] out IExecutor<CommandContext>? executor, [NotNullWhen(true)] out string[]? boundArgs)
    {
        foreach (var exec in executors)
        {
            var methodParams = exec.GetParameters();
            var bound = BindArguments(methodParams, args);

            if (bound is null)
                continue;

            var success = true;

            for (int i = 0; i < bound.Length && success; i++)
            {
                var paramType = methodParams[i].ParameterType;

                success = CommandHandler.IsValidArgumentType(paramType)
                    && CommandHandler.GetArgumentParser(paramType).TryParseArgument(bound[i], context, out _);
            }

            if (success)
            {
                executor = exec;
                boundArgs = bound;
                return true;
            }
        }

        executor = null;
        boundArgs = null;
        return false;
    }

    /// <summary>
    /// Pairs the words of a command with <paramref name="parameters"/>. A trailing parameter marked with
    /// <see cref="RemainingAttribute"/> receives the rest of the words joined by spaces.
    /// </summary>
    /// <returns>One argument per parameter, or <see langword="null"/> if the word count does not fit.</returns>
    private static string[]? BindArguments(ParameterInfo[] parameters, string[] args)
    {
        if (args.Length == parameters.Length)
            return args;

        if (args.Length < parameters.Length || parameters.Length == 0
            || parameters[^1].GetCustomAttribute<RemainingAttribute>() is null)
            return null;

        var last = parameters.Length - 1;

        return [.. args[..last], string.Join(' ', args[last..])];
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

        if (await this.FindFailedCheckAsync(commandExecutor, context) is BaseExecutionCheckAttribute failed)
        {
            // TODO: Tell user what arg failed?
            throw failed switch
            {
                RequirePermissionAttribute r => new NoPermissionException(r.RequiredPermissions, r.CheckType),
                _ => new CommandExecutionCheckException($"One or more execution checks failed."),
            };
        }

        // await the command with it's args
        await commandExecutor.Execute(serviceScope.ServiceProvider, context, parsedargs);
    }

    /// <summary>
    /// Runs the checks of this command, of the groups it belongs to and of <paramref name="overload"/>, so that a
    /// group's or command's restrictions also cover its subcommands and overloads.
    /// </summary>
    /// <returns>The first check that fails, or <see langword="null"/> if every check passes.</returns>
    internal async Task<BaseExecutionCheckAttribute?> FindFailedCheckAsync(IExecutor<CommandContext> overload, CommandContext context)
    {
        var checks = SelfAndGroups().SelectMany(command => command.ExecutionChecks)
            .Concat(overload.GetCustomAttributes<BaseExecutionCheckAttribute>());

        foreach (var check in checks)
        {
            if (!await check.RunChecksAsync(context))
                return check;
        }

        return null;
    }

    private IEnumerable<Command> SelfAndGroups()
    {
        for (var command = this; command is not null; command = command.Parent)
            yield return command;
    }

    public override string ToString() => $"{CommandHelpers.DefaultPrefix}{GetQualifiedName()}";
}
