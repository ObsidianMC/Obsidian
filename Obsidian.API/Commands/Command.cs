using Microsoft.Extensions.DependencyInjection;
using Obsidian.API.Commands.Exceptions;
using Obsidian.API.Plugins;
using Obsidian.API.Utilities;
using Obsidian.API.Utilities.Interfaces;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Obsidian.API.Commands;
/// <summary>
/// A registered command or command group, with its overloads and the checks that decide who may run it.
/// </summary>
public sealed class Command
{
    /// <summary>
    /// Gets the kinds of sender that may run the command, set with <see cref="IssuerScopeAttribute"/>. A command's
    /// groups must allow the sender too.
    /// </summary>
    public CommandIssuers AllowedIssuers { get; init; }

    /// <summary>
    /// Gets the command handler the command is registered with.
    /// </summary>
    public required ICommandHandler CommandHandler { get; init; }
    /// <summary>
    /// Gets the plugin that registered the command, or <see langword="null"/> for the server's own commands.
    /// </summary>
    public required IPluginContainer? PluginContainer { get; init; }
    /// <summary>
    /// Gets the command's name, typed after the slash (or after its group's name for a subcommand).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets other names the command can be typed as.
    /// </summary>
    public ImmutableArray<string> Aliases { get; init; } = [];
    /// <summary>
    /// Gets the description set with <see cref="CommandInfoAttribute"/>, or <see langword="null"/>.
    /// </summary>
    public string? Description { get; init; }
    /// <summary>
    /// Gets the usage text set with <see cref="CommandInfoAttribute"/>, shown to a sender whose arguments match no
    /// overload.
    /// </summary>
    public string? Usage { get; init; }

    /// <summary>
    /// Gets the methods that can run the command, tried in order until one's parameters accept the arguments.
    /// </summary>
    public List<IExecutor<CommandContext>> Overloads { get; init; } = [];
    /// <summary>
    /// Gets the checks every sender must pass to run the command or, for a group, any of its subcommands.
    /// </summary>
    public ImmutableArray<BaseExecutionCheckAttribute> ExecutionChecks { get; init; } = [];

    /// <summary>
    /// Gets the group the command belongs to, or <see langword="null"/> for a top-level command.
    /// </summary>
    public Command? Parent { get; init; }

    internal Command() { }

    /// <summary>
    /// Returns whether the first word of <paramref name="input"/> is this command's name or one of its aliases, and the
    /// command belongs to <paramref name="parent"/>.
    /// </summary>
    /// <param name="input">The words of the command line, starting at the word to match.</param>
    /// <param name="parent">The group the command must belong to, or <see langword="null"/> for a top-level command.</param>
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

        while (c.Parent is not null)
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

        // Overloads whose parameters can't take as many words are skipped as the words are bound.
        var executors = Overloads;

        // Find matching overload
        if (executors is null)
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
    /// Pairs the words of a command with <paramref name="parameters"/>, each taking as many words as its parser reads
    /// (see <see cref="BaseArgumentParser.WordCount"/>). A trailing parameter marked with
    /// <see cref="RemainingAttribute"/> receives the rest of the words joined by spaces.
    /// </summary>
    /// <returns>One argument per parameter, or <see langword="null"/> if the word count does not fit.</returns>
    private string[]? BindArguments(ParameterInfo[] parameters, string[] args) =>
        BindArguments(parameters, args, type => this.CommandHandler.IsValidArgumentType(type)
            ? this.CommandHandler.GetArgumentParser(type).WordCount
            : 1);

    /// <inheritdoc cref="BindArguments(ParameterInfo[], string[])"/>
    /// <param name="parameters">The parameters to bind the words to.</param>
    /// <param name="args">The words the command was issued with.</param>
    /// <param name="wordCount">How many words an argument of a type takes.</param>
    internal static string[]? BindArguments(ParameterInfo[] parameters, IReadOnlyList<string> args, Func<Type, int> wordCount)
    {
        var bound = new string[parameters.Length];
        var used = 0;

        for (var i = 0; i < parameters.Length; i++)
        {
            if (i == parameters.Length - 1 && parameters[i].GetCustomAttribute<RemainingAttribute>() is not null)
            {
                if (used >= args.Count)
                    return null;

                bound[i] = string.Join(' ', args.Skip(used));
                return bound;
            }

            var count = Math.Max(1, wordCount(parameters[i].ParameterType));
            if (used + count > args.Count)
                return null;

            bound[i] = string.Join(' ', args.Skip(used).Take(count));
            used += count;
        }

        return used == args.Count ? bound : null;
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

    /// <summary>
    /// Returns the command's full name with the command prefix, like <c>/team add</c>.
    /// </summary>
    public override string ToString() => $"{CommandHelpers.DefaultPrefix}{GetQualifiedName()}";
}
