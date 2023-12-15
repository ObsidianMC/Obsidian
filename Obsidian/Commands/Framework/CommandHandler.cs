using Microsoft.Extensions.Logging;
using Obsidian.API.Plugins;
using Obsidian.API.Utilities;
using Obsidian.Commands.Builders;
using Obsidian.Commands.Framework.Entities;
using Obsidian.Commands.Framework.Exceptions;
using Obsidian.Commands.Parsers;
using Obsidian.Plugins;
using Obsidian.Plugins.ServiceProviders;
using System.Reflection;

namespace Obsidian.Commands.Framework;

public sealed class CommandHandler
{
    private readonly PluginManager? pluginManager;
    private readonly ILogger? logger;
    private readonly List<Command> _commands;
    private readonly CommandParser _commandParser;
    private readonly List<BaseArgumentParser> _argumentParsers;

    public CommandHandler(PluginManager? pluginManager = null, ILogger<CommandHandler>? logger = null)
    {
        _commandParser = new CommandParser(CommandHelpers.DefaultPrefix);
        _commands = [];
        _argumentParsers = [new LocationTypeParser(), new PlayerTypeParser()];

        // Find all predefined argument parsers
        var parsers = typeof(StringArgumentParser).Assembly.GetTypes().Where(type => typeof(BaseArgumentParser).IsAssignableFrom(type) && !type.IsAbstract);

        foreach (var parser in parsers)
        {
            if (Activator.CreateInstance(parser) is BaseArgumentParser<object> parserInstance)
            {
                _argumentParsers.Add(parserInstance);
            }
        }

        this.pluginManager = pluginManager;
        this.logger = logger;
    }

    public (int id, string mctype) FindMinecraftType(Type type)
    {
        var parserType = _argumentParsers.FirstOrDefault(x => x.GetType().BaseType?.GetGenericArguments()[0] == type)?.GetType();

        if (parserType is null || Activator.CreateInstance(parserType) is not BaseArgumentParser parserInstance)
            throw new Exception("No such parser registered!");

        return (parserInstance.Id, parserInstance.ParserIdentifier);
    }

    public bool IsValidArgumentType(Type argumentType) =>
        this._argumentParsers.Any(x => x.GetType().BaseType?.GetGenericArguments().First() == argumentType);

    public BaseArgumentParser GetArgumentParser(Type argumentType) =>
        this._argumentParsers.First(x => x.GetType().BaseType?.GetGenericArguments().First() == argumentType);

    public Command[] GetAllCommands() => _commands.ToArray();

    public void AddArgumentParser(BaseArgumentParser parser) => _argumentParsers.Add(parser);

    public void UnregisterPluginCommands(PluginContainer plugin) => _commands.RemoveAll(x => x.PluginContainer == plugin);

    public void RegisterCommandClass<T>(PluginContainer plugin) => RegisterCommandClass(plugin, typeof(T));

    public void RegisterCommandClass(PluginContainer plugin, Type type)
    {
        RegisterSubgroups(type, plugin);
        RegisterSubcommands(type, plugin);
    }

    //TODO rework.
    public void RegisterCommands(PluginContainer pluginContainer)
    {
        // Registering commands from within the plugin
        this.RegisterCommandClass(pluginContainer, pluginContainer.Plugin.GetType());

        // Registering commands found in the plugin assembly
        var commandRoots = pluginContainer.Plugin.GetType().Assembly.GetTypes().Where(x => x.GetCustomAttributes(false).Any(y => y.GetType() == typeof(CommandRootAttribute)));
        foreach (var root in commandRoots)
        {
            this.RegisterCommandClass(pluginContainer, root);
        }
    }

    public object? CreateCommandRootInstance(Type? type, PluginContainer plugin)
    {
        if (type is null)
            return null;

        object? instance = Activator.CreateInstance(type);
        if (instance is null)
            return null;

        var injectables = type.GetProperties().Where(x => x.GetCustomAttribute<InjectAttribute>() != null);
        foreach (var injectable in injectables)
        {
            //Plugins should stick to services and not be able to have access to other plugin base class.
            //if (injectable.PropertyType == typeof(PluginBase) || injectable.PropertyType == plugin.Plugin.GetType())
            //{
            //    injectable.SetValue(instance, plugin.Plugin);
            //}
            //else
            //{
            //}

            PluginServiceHandler.InjectServices(pluginManager!.PluginServiceProvider, instance, plugin, pluginManager.logger, pluginManager.loggerProvider);
        }

        return instance;
    }

    private void RegisterSubgroups(Type type, PluginContainer pluginContainer, Command? parent = null)
    {
        // find all command groups under this command
        var subtypes = type.GetNestedTypes().Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandGroupAttribute)));

        foreach (var st in subtypes)
        {
            var group = st.GetCustomAttribute<CommandGroupAttribute>()!;
            // Get command name from first constructor argument for command attribute.
            var name = group.GroupName;
            // Get aliases
            var aliases = group.Aliases;

            var checks = st.GetCustomAttributes<BaseExecutionCheckAttribute>();

            var info = st.GetCustomAttribute<CommandInfoAttribute>();
            var issuers = st.GetCustomAttribute<IssuerScopeAttribute>()?.Issuers ?? CommandHelpers.DefaultIssuerScope;

            var command = CommandBuilder.Create(name)
              .WithDescription(info?.Description)
              .WithParent(parent)
              .WithUsage(info?.Usage)
              .AddAliases(aliases)
              .AddExecutionChecks(checks.ToArray())
              .CanIssueAs(issuers)
              .Build(this, pluginContainer);

            RegisterSubgroups(st, pluginContainer, command);
            RegisterSubcommands(st, pluginContainer, command);

            _commands.Add(command);
        }
    }

    private void RegisterSubcommands(Type type, PluginContainer pluginContainer, Command? parent = null)
    {
        // loop through methods and find valid commands
        var methods = type.GetMethods();

        if (parent is not null)
        {
            // Adding all methods with GroupCommand attribute
            parent.Overloads!.AddRange(methods.Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(GroupCommandAttribute))));
        }

        // Selecting all methods that have the CommandAttribute.
        foreach (var m in methods.Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandAttribute))))
        {
            // Get command name from first constructor argument for command attribute.
            var cmd = m.GetCustomAttribute<CommandAttribute>();
            if (cmd is null)
                continue; // TODO Log warning (?)

            var name = cmd.CommandName;
            // Get aliases
            var aliases = cmd.Aliases;
            var checks = m.GetCustomAttributes<BaseExecutionCheckAttribute>();

            var info = m.GetCustomAttribute<CommandInfoAttribute>();
            var issuers = m.GetCustomAttribute<IssuerScopeAttribute>()?.Issuers ?? CommandHelpers.DefaultIssuerScope;

            var command = CommandBuilder.Create(name)
                .WithDescription(info?.Description)
                .WithParent(parent)
                .WithUsage(info?.Usage)
                .AddAliases(aliases)
                .AddOverload(m)
                .AddOverloads(methods.Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandOverloadAttribute)) && x.Name == m.Name))
                .AddExecutionChecks(checks)
                .CanIssueAs(issuers)
                .Build(this, pluginContainer);

            _commands.Add(command);
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

    private async Task ExecuteCommand(string[] command, CommandContext ctx)
    {
        Command? cmd = default;
        var args = command;

        // Search for correct Command class in this._commands.
        while (_commands.Any(x => x.CheckCommand(args, cmd)))
        {
            cmd = _commands.First(x => x.CheckCommand(args, cmd));
            args = args.Skip(1).ToArray();
        }

        if (cmd is not null)
        {
            ctx.Plugin = cmd.PluginContainer?.Plugin;
            await cmd.ExecuteAsync(ctx, args);
        }
        else
            throw new CommandNotFoundException("No such command was found!");
    }
}
