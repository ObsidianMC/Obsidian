using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Obsidian.API.Commands;
using Obsidian.API.Commands.ArgumentParsers;
using Obsidian.API.Commands.Exceptions;
using Obsidian.API.Plugins;
using Obsidian.API.Utilities.Interfaces;
using Obsidian.Commands.Builders;
using Obsidian.Commands.Modules;
using Obsidian.Plugins;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Obsidian.Commands.Framework;

public sealed class CommandHandler : ICommandHandler
{
    // Vanilla's suggestion handler sends at most this many suggestions.
    private const int MaxSuggestions = 1000;

    internal readonly ILogger logger;

    // Replaced as a whole, so network threads reading it never see a registration half done.
    private ImmutableArray<Command> _commands = [];
    private readonly CommandParser _commandParser;
    // Concurrent because enum parsers are added on first use, which can happen on network threads.
    private readonly ConcurrentDictionary<Type, BaseArgumentParser> _argumentParsers;

    public IServiceProvider ServiceProvider { get; }

    public CommandHandler(IServiceProvider serviceProvider, ILogger<CommandHandler> logger)
    {
        _commandParser = new CommandParser(CommandHelpers.DefaultPrefix);

        // Find all predefined argument parsers
        var parsers = typeof(StringArgumentParser).Assembly.GetTypes()
            .Where(type => typeof(BaseArgumentParser).IsAssignableFrom(type) && !type.IsAbstract)
            .Where(type => type.BaseType?.IsGenericType is true && type.BaseType.GetGenericArguments().Length != 0)
            .Select(x => (Activator.CreateInstance(x) as BaseArgumentParser)!);

        _argumentParsers = new(parsers.Select(x => KeyValuePair.Create(x.GetType().BaseType!.GetGenericArguments().First(), x)));

        this.ServiceProvider = serviceProvider;
        this.logger = logger;
    }

    public (int id, string mctype) FindMinecraftType(Type type)
    {
        if (!this.TryGetArgumentParser(type, out var parser))
            throw new Exception($"No valid argument parser found for type {type.Name}!");

        return (parser.Id, parser.Identifier);
    }

    public bool IsValidArgumentType(Type argumentType) =>
        this.TryGetArgumentParser(argumentType, out _);

    public BaseArgumentParser GetArgumentParser(Type argumentType) =>
        this.TryGetArgumentParser(argumentType, out var parser)
            ? parser
            : throw new ArgumentException($"No parser registered for type {argumentType}");

    /// <summary>
    /// Finds the parser registered for <paramref name="type"/>. An enum without one gets an
    /// <see cref="EnumArgumentParser"/>, kept for later lookups, so a plugin's parser for an enum must be added first.
    /// </summary>
    private bool TryGetArgumentParser(Type type, [NotNullWhen(true)] out BaseArgumentParser? parser)
    {
        if (this._argumentParsers.TryGetValue(type, out parser))
            return true;

        if (!type.IsEnum)
            return false;

        parser = this._argumentParsers.GetOrAdd(type, enumType => new EnumArgumentParser(enumType));
        return true;
    }

    public Command[] GetAllCommands() => _commands.ToArray();

    private void AddCommand(Command command) => ImmutableInterlocked.Update(ref _commands, commands => commands.Add(command));

    public void RegisterCommand(PluginContainer? pluginContainer, string name, Delegate commandDelegate)
    {
        var method = commandDelegate.Method;

        var commandInfo = method.GetCustomAttribute<CommandInfoAttribute>();
        var checks = method.GetCustomAttributes<BaseExecutionCheckAttribute>();
        var issuers = method.GetCustomAttribute<IssuerScopeAttribute>()?.Issuers ?? CommandHelpers.DefaultIssuerScope;

        var executor = new CommandDelegateExecutor
        {
            Logger = this.logger,
            PluginContainer = pluginContainer,
            MethodDelegate = commandDelegate,
        };

        var command = CommandBuilder.Create(name)
             .WithDescription(commandInfo?.Description)
             .WithUsage(commandInfo?.Usage)
             .AddExecutionChecks(checks)
             .CanIssueAs(issuers)
             .AddOverload(executor)
             .Build(this, pluginContainer);

        AddCommand(command);
    }

    public bool TryAddArgumentParser<TValue>(BaseArgumentParser<TValue> parser) =>
        _argumentParsers.TryAdd(typeof(TValue), parser);

    public void UnregisterPluginCommands(IPluginContainer? plugin) =>
        ImmutableInterlocked.Update(ref _commands, commands => commands.RemoveAll(x => x.PluginContainer == plugin));

    public void RegisterCommandClass<T>(IPluginContainer? plugin) => RegisterCommandClass(plugin, typeof(T));

    public void RegisterCommandClass(IPluginContainer? plugin, Type moduleType)
    {
        if (moduleType.GetCustomAttribute<CommandGroupAttribute>() is not null)
        {
            this.RegisterGroupCommand(moduleType, plugin, null);
            return;
        }

        RegisterSubgroups(moduleType, plugin);
        RegisterSubcommands(moduleType, plugin);
    }

    public void RegisterCommands(IPluginContainer? pluginContainer = null)
    {
        var assembly = pluginContainer?.PluginAssembly ?? Assembly.GetExecutingAssembly();

        // The integrated server's commands are registered by the server when it runs as one.
        var commandRoots = assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(CommandModuleBase)) && type != typeof(IntegratedCommandModule));

        foreach (var root in commandRoots)
        {
            if (root == typeof(Obsidian.Commands.Modules.MobTestCommandModule))
                continue;
            this.RegisterCommandClass(pluginContainer, root);
        }
    }

    private void RegisterGroupCommand(Type moduleType, IPluginContainer? pluginContainer, Command? parent = null)
    {
        var group = moduleType.GetCustomAttribute<CommandGroupAttribute>()!;
        // Get command name from first constructor argument for command attribute.
        var name = group.GroupName;
        // Get aliases
        var aliases = group.Aliases;

        var checks = moduleType.GetCustomAttributes<BaseExecutionCheckAttribute>();

        var info = moduleType.GetCustomAttribute<CommandInfoAttribute>();
        var issuers = moduleType.GetCustomAttribute<IssuerScopeAttribute>()?.Issuers ?? CommandHelpers.DefaultIssuerScope;

        var command = CommandBuilder.Create(name)
          .WithDescription(info?.Description)
          .WithParent(parent)
          .WithUsage(info?.Usage)
          .AddAliases(aliases)
          .AddExecutionChecks(checks)
          .CanIssueAs(issuers)
          .Build(this, pluginContainer);

        RegisterSubgroups(moduleType, pluginContainer, command);
        RegisterSubcommands(moduleType, pluginContainer, command);

        AddCommand(command);
    }

    private void RegisterSubgroups(Type moduleType, IPluginContainer? pluginContainer, Command? parent = null)
    {
        // find all command groups under this command
        var subModules = moduleType.GetNestedTypes()
            .Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandGroupAttribute)));

        foreach (var subModule in subModules)
        {
            this.RegisterGroupCommand(subModule, pluginContainer, parent);
        }
    }

    private void RegisterSubcommands(Type moduleType, IPluginContainer? pluginContainer, Command? parent = null)
    {
        // loop through methods and find valid commands
        var methods = moduleType.GetMethods();

        if (parent is not null)
        {
            // Adding all methods with GroupCommand attribute
            var overloads = methods.Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(GroupCommandAttribute)))
                .Select(x => ObjectMethodExecutor.Create(x, moduleType.GetTypeInfo()))
                .Select(x => new CommandExecutor
                {
                    Logger = this.logger,
                    PluginContainer = pluginContainer,
                    MethodExecutor = x,
                    ModuleType = moduleType,
                    ModuleFactory = ActivatorUtilities.CreateFactory(moduleType, Type.EmptyTypes)
                });

            parent.Overloads!.AddRange(overloads);
        }

        // Selecting all methods that have the CommandAttribute. Overloads join the command of the same method name
        // below, so they aren't registered as commands of their own even when they repeat the CommandAttribute.
        foreach (var method in methods.Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandAttribute))
            && !x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandOverloadAttribute))))
        {
            // Get command name from first constructor argument for command attribute.
            var cmd = method.GetCustomAttribute<CommandAttribute>();
            if (cmd is null)
                continue; // TODO Log warning (?)

            var name = cmd.CommandName;
            // Get aliases
            var aliases = cmd.Aliases;
            var checks = method.GetCustomAttributes<BaseExecutionCheckAttribute>();

            var info = method.GetCustomAttribute<CommandInfoAttribute>();
            var issuers = method.GetCustomAttribute<IssuerScopeAttribute>()?.Issuers ?? CommandHelpers.DefaultIssuerScope;

            var executor = new CommandExecutor
            {
                Logger = this.logger,
                PluginContainer = pluginContainer,
                MethodExecutor = ObjectMethodExecutor.Create(method, moduleType.GetTypeInfo()),
                ModuleType = moduleType,
                ModuleFactory = ActivatorUtilities.CreateFactory(moduleType, Type.EmptyTypes)
            };

            var overloads = methods
                .Where(overload => overload.CustomAttributes.Any(attribute => attribute.AttributeType == typeof(CommandOverloadAttribute))
                    && overload.Name == method.Name)
                .Select(x => ObjectMethodExecutor.Create(x, moduleType.GetTypeInfo()))
                .Select(x => new CommandExecutor
                {
                    Logger = this.logger,
                    PluginContainer = pluginContainer,
                    MethodExecutor = x,
                    ModuleType = moduleType,
                    ModuleFactory = ActivatorUtilities.CreateFactory(moduleType, Type.EmptyTypes)
                });

            var command = CommandBuilder.Create(name)
                .WithDescription(info?.Description)
                .WithParent(parent)
                .WithUsage(info?.Usage)
                .AddAliases(aliases)
                .AddOverload(executor)
                .AddOverloads(overloads)
                .AddExecutionChecks(checks)
                .CanIssueAs(issuers)
                .Build(this, pluginContainer);

            AddCommand(command);
        }
    }

    public async Task ProcessCommand(CommandContext ctx)
    {
        // split the command message into command and args.
        if (_commandParser.IsCommandQualified(ctx.Message, out ReadOnlyMemory<char> qualified))
        {
            // if string is "command-qualified" we'll try to execute it.
            string[] command = CommandParser.SplitQualifiedString(qualified); // first, parse the command

            try
            {
                await ExecuteCommand(command, ctx);
            }
            catch (CommandExecutionCheckException ex)
            {
                await ProvideFeedbackToSender(ctx, ex);
            }
        }
    }

    /// <summary>
    /// Suggests command and subcommand names and argument values from each argument's <see cref="ISuggestionProvider"/>.
    /// Arguments typed before the one at the cursor must parse for an overload to be suggested, and only commands and
    /// overloads the sender may run are considered. Suggestions for a trailing <see cref="RemainingAttribute"/>
    /// parameter cover every word it takes.
    /// </summary>
    public async Task<CommandCompletion> CompleteAsync(CommandContext ctx)
    {
        var input = ctx.Message;
        var offset = input.StartsWith(_commandParser.Prefix, StringComparison.Ordinal) ? _commandParser.Prefix.Length : 0;
        var words = CommandParser.SplitWords(input.AsSpan(offset));
        var typedCount = words.Count - 1;
        var (partialStart, partial) = words[^1];
        var empty = new CommandCompletion(offset + partialStart, input.Length - offset - partialStart, []);

        var commands = GetAllCommands();
        Command? command = null;
        var used = 0;

        // Follow command and subcommand names as ExecuteCommand does; the words after them are arguments.
        while (used < typedCount && commands.FirstOrDefault(x => x.CheckCommand([words[used].Value], command)) is Command next)
        {
            if (!await CanUseAsync(next, ctx))
                return empty;

            command = next;
            used++;
        }

        if (command is null && typedCount > 0)
            return empty;

        var groups = new List<SuggestionGroup>();

        if (used == typedCount)
        {
            var names = new List<CommandSuggestion>();

            foreach (var child in commands.Where(x => x.Parent == command))
            {
                if (await CanSuggestNameAsync(child, commands, ctx))
                    names.AddRange(child.Aliases.Prepend(child.Name).Select(name => new CommandSuggestion(name)));
            }

            groups.Add(new SuggestionGroup(partialStart, partial, IsWord: false, names));
        }

        if (command is not null)
        {
            foreach (var overload in command.Overloads)
            {
                if (await SuggestArgumentAsync(command, overload, words[used..], ctx) is { } group)
                    groups.Add(group);
            }
        }

        // Like vanilla, drop suggestions that are already typed in full.
        var matches = groups.SelectMany(group => group.Suggestions
                .Where(x => x.Text.StartsWith(group.Typed, StringComparison.OrdinalIgnoreCase) && x.Text != group.Typed)
                .Select(x => (Start: offset + group.Start, Suggestion: group.IsWord ? QuoteIfNeeded(x, input.AsSpan(offset + group.Start).StartsWith('"')) : x)))
            .ToArray();

        if (matches.Length == 0)
            return empty;

        // Like vanilla, suggestions for text that starts later are widened to replace the same text as the earliest by
        // prepending the typed text in between, and the reply is capped at 1000 entries.
        var start = matches.Min(x => x.Start);
        CommandSuggestion[] suggestions = [.. matches
            .Select(x => x.Suggestion with { Text = input[start..x.Start] + x.Suggestion.Text })
            .DistinctBy(x => x.Text)
            .OrderBy(x => x.Text, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSuggestions)];

        return new CommandCompletion(start, input.Length - start, suggestions);
    }

    /// <summary>
    /// Suggests values for the parameter of <paramref name="overload"/> that the last of <paramref name="words"/> is
    /// typed into, if the words before it parse and the sender passes the overload's checks.
    /// </summary>
    /// <param name="words">The command's arguments, ending with the word at the cursor.</param>
    /// <returns>The argument's suggestions, or <see langword="null"/> if there are none.</returns>
    private async Task<SuggestionGroup?> SuggestArgumentAsync(
        Command command, IExecutor<CommandContext> overload, List<(int Start, string Value)> words, CommandContext ctx)
    {
        // Parsers, checks and the provider see the command's plugin, as when it runs.
        ctx.Plugin = command.PluginContainer?.Plugin;

        var parameters = overload.GetParameters();
        var index = words.Count - 1;

        // A trailing [Remaining] parameter takes every word from its first, as when the command runs.
        if (index >= parameters.Length && parameters.Length > 0 && parameters[^1].GetCustomAttribute<RemainingAttribute>() is not null)
            index = parameters.Length - 1;

        if (index >= parameters.Length || FindSuggestionProvider(parameters[index]) is not ISuggestionProvider provider)
            return null;

        for (var i = 0; i < index; i++)
        {
            var type = parameters[i].ParameterType;

            if (!IsValidArgumentType(type) || !GetArgumentParser(type).TryParseArgument(words[i].Value, ctx, out _))
                return null;
        }

        if (await command.FindFailedCheckAsync(overload, ctx) is not null)
            return null;

        var typed = string.Join(' ', words[index..].Select(word => word.Value));
        var isWord = parameters[index].GetCustomAttribute<RemainingAttribute>() is null;

        return new SuggestionGroup(words[index].Start, typed, isWord, await provider.GetSuggestionsAsync(ctx));
    }

    /// <summary>
    /// Quotes a suggestion for a one-word argument if the command parser would otherwise split or unescape it, or if
    /// the word being replaced was typed with an opening quote, so that the completed text reads back as the suggestion.
    /// </summary>
    private static CommandSuggestion QuoteIfNeeded(CommandSuggestion suggestion, bool typedQuote)
    {
        if (!typedQuote && !suggestion.Text.AsSpan().ContainsAny(' ', '"', '\\'))
            return suggestion;

        var escaped = suggestion.Text.Replace("\\", "\\\\").Replace("\"", "\\\"");

        return suggestion with { Text = $"\"{escaped}\"" };
    }

    /// <summary>Suggestions for the text from <see cref="Start"/> (after the prefix) to the cursor.</summary>
    /// <param name="Start">Where the replaced text starts, after the command prefix.</param>
    /// <param name="Typed">The replaced text once split into words, which suggestions are matched against.</param>
    /// <param name="IsWord">
    /// Whether the suggestions fill one argument word, so they may need quoting, rather than a command name or the
    /// rest of the line.
    /// </param>
    private readonly record struct SuggestionGroup(int Start, string Typed, bool IsWord, IEnumerable<CommandSuggestion> Suggestions);

    /// <summary>
    /// Finds what suggests values for <paramref name="parameter"/>: its suggestion attribute, or else its argument parser.
    /// </summary>
    private ISuggestionProvider? FindSuggestionProvider(ParameterInfo parameter) =>
        parameter.GetCustomAttribute<BaseSuggestionProviderAttribute>()
            ?? (IsValidArgumentType(parameter.ParameterType) ? GetArgumentParser(parameter.ParameterType) as ISuggestionProvider : null);

    /// <summary>
    /// Whether <paramref name="command"/> may be offered by name: the sender can use it, and it has a usable overload
    /// or subcommands, which are checked once they are typed.
    /// </summary>
    private static async Task<bool> CanSuggestNameAsync(Command command, Command[] commands, CommandContext ctx)
    {
        if (!await CanUseAsync(command, ctx))
            return false;

        if (command.Overloads.Count == 0 || commands.Any(x => x.Parent == command))
            return true;

        foreach (var overload in command.Overloads)
        {
            if (await command.FindFailedCheckAsync(overload, ctx) is null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the sender may use <paramref name="command"/> as far as it and the groups already checked go. Its checks
    /// see the command's plugin in the context, as when it runs.
    /// </summary>
    private static async Task<bool> CanUseAsync(Command command, CommandContext ctx)
    {
        ctx.Plugin = command.PluginContainer?.Plugin;

        if (!command.AllowedIssuers.HasFlag(ctx.Sender.Issuer))
            return false;

        foreach (var check in command.ExecutionChecks)
        {
            if (!await check.RunChecksAsync(ctx))
                return false;
        }

        return true;
    }

    private static async Task ProvideFeedbackToSender(CommandContext ctx, CommandExecutionCheckException ex)
    {
        switch (ex)
        {
            case NoPermissionException:
                await ctx.Sender.SendMessageAsync(ChatMessage.Simple("You are not allowed to execute this command", ChatColor.Red));
                break;
            default:
                await ctx.Sender.SendMessageAsync(ChatMessage.Simple(ex.Message, ChatColor.Red));
                break;
        }
    }

    private async Task ExecuteCommand(string[] args, CommandContext ctx)
    {
        Command? cmd = default;

        var commands = _commands;

        // Search for correct Command class in the registered commands.
        while (commands.Any(x => x.CheckCommand(args, cmd)))
        {
            cmd = commands.First(x => x.CheckCommand(args, cmd));
            args = Enumerable.Skip(args, 1).ToArray();
        }

        if (cmd is not null)
        {
            ctx.Plugin = cmd.PluginContainer?.Plugin;
            await cmd.ExecuteAsync(ctx, args);
        }
        else
            await ctx.Sender.SendMessageAsync("No such command was found!");
    }
}
