using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Obsidian.API.Commands;
using Obsidian.API.Commands.ArgumentParsers;
using Obsidian.API.Plugins;
using Obsidian.Commands.Builders;
using Obsidian.Commands.Framework.Exceptions;
using Obsidian.Plugins;
using System.Reflection;

namespace Obsidian.Commands.Framework;

public sealed class CommandHandler : ICommandHandler
{
    internal readonly ILogger logger;

    private readonly List<Command> _commands;
    private readonly CommandParser _commandParser;
    private readonly List<BaseArgumentParser> _argumentParsers;

    public IServiceProvider ServiceProvider { get; }

    public CommandHandler(IServiceProvider serviceProvider, ILogger<CommandHandler> logger)
    {
        _commandParser = new CommandParser(CommandHelpers.DefaultPrefix);
        _commands = [];

        // Find all predefined argument parsers
        var parsers = typeof(StringArgumentParser).Assembly.GetTypes()
            .Where(type => typeof(BaseArgumentParser).IsAssignableFrom(type) && !type.IsAbstract)
            .Select(x => (Activator.CreateInstance(x) as BaseArgumentParser)!);

        _argumentParsers = parsers.OrderBy(x => x.Id).ToList();

        this.ServiceProvider = serviceProvider;
        this.logger = logger;
    }

    public (int id, string mctype) FindMinecraftType(Type type)
    {
        var parserType = _argumentParsers.FirstOrDefault(x => x.GetType().BaseType?.GetGenericArguments()[0] == type)?.GetType();

        if (parserType is null || Activator.CreateInstance(parserType) is not BaseArgumentParser parserInstance)
            throw new Exception($"No such parser registered! {type}");

        return (parserInstance.Id, parserInstance.ParserIdentifier);
    }

    public bool IsValidArgumentType(Type argumentType) =>
        this._argumentParsers.Any(x => x.GetType().BaseType?.GetGenericArguments().First() == argumentType);

    public BaseArgumentParser GetArgumentParser(Type argumentType) =>
        this._argumentParsers.First(x => x.GetType().BaseType?.GetGenericArguments().First() == argumentType);

    public Command[] GetAllCommands() => _commands.ToArray();

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

        _commands.Add(command);
    }

    public void AddArgumentParser(BaseArgumentParser parser) => _argumentParsers.Add(parser);

    public void UnregisterPluginCommands(IPluginContainer? plugin) => _commands.RemoveAll(x => x.PluginContainer == plugin);

    public void RegisterCommandClass<T>(IPluginContainer? plugin) => RegisterCommandClass(plugin, typeof(T));

    public void RegisterCommandClass(IPluginContainer? plugin, Type moduleType)
    {
        if (moduleType.GetCustomAttribute<CommandGroupAttribute>() != null)
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

        var commandRoots = assembly.GetTypes().Where(x => x.IsSubclassOf(typeof(CommandModuleBase)));

        foreach (var root in commandRoots)
        {
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

        _commands.Add(command);
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

        // Selecting all methods that have the CommandAttribute.
        foreach (var method in methods.Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandAttribute))))
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

            var overloads = methods.Where(x => x.CustomAttributes.Any(y => y.AttributeType == typeof(CommandOverloadAttribute)) && x.Name == method.Name)
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

    private async Task ExecuteCommand(string[] args, CommandContext ctx)
    {
        Command? cmd = default;

        // Search for correct Command class in this._commands.
        while (_commands.Any(x => x.CheckCommand(args, cmd)))
        {
            cmd = _commands.First(x => x.CheckCommand(args, cmd));
            args = Enumerable.Skip(args, 1).ToArray();
        }

        if (cmd is not null)
        {
            ctx.Plugin = cmd.PluginContainer?.Plugin;
            await cmd.ExecuteAsync(ctx, args);
        }
        else
        {
            await ctx.Sender.SendMessageAsync("No such command was found!");
            this.logger.LogError(new CommandNotFoundException("No such command was found!"), "An error has occured while trying to execute command: {args}", args);
        }
    }
}
