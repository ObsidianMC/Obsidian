using Obsidian.API.Commands;
using Obsidian.API.Plugins;
using Obsidian.API.Utilities.Interfaces;
using Obsidian.Commands.Framework;

namespace Obsidian.Commands.Builders;
public sealed class CommandBuilder
{
    private readonly List<string> aliases = [];
    private readonly List<IExecutor<CommandContext>> overloads = [];
    private readonly List<BaseExecutionCheckAttribute> checks = [];

    public string Name { get; }

    public string? Description { get; private set; } = default!;

    public string? Usage { get; private set; } = default!;

    public Command? Parent { get; private set; } = default!;

    public CommandIssuers Issuers { get; private set; }

    public IReadOnlyCollection<IExecutor<CommandContext>> Overloads => this.overloads.AsReadOnly();
    public IReadOnlyCollection<string> Aliases => this.aliases.AsReadOnly();
    public IReadOnlyCollection<BaseExecutionCheckAttribute> Checks => this.checks.AsReadOnly();

    private CommandBuilder(string name)
    {
        this.Name = name;
    }

    public static CommandBuilder Create(string name) => new(name);

    public CommandBuilder WithDescription(string? description)
    {
        this.Description = description;

        return this;
    }

    public CommandBuilder WithParent(Command? parent)
    {
        this.Parent = parent;
        return this;
    }

    public CommandBuilder AddOverload(IExecutor<CommandContext> commandExecutor)
    {
        this.overloads.Add(commandExecutor);

        return this;
    }

    public CommandBuilder AddOverloads(params IExecutor<CommandContext>[] executors)
    {
        this.overloads.AddRange(executors);

        return this;
    }

    public CommandBuilder AddOverloads(IEnumerable<IExecutor<CommandContext>> executors)
    {
        this.overloads.AddRange(executors);

        return this;
    }

    public CommandBuilder AddAlias(string alias)
    {
        ArgumentException.ThrowIfNullOrEmpty(alias);

        this.aliases.Add(alias);

        return this;
    }

    public CommandBuilder AddAliases(params string[] aliases)
    {
        this.aliases.AddRange(aliases);

        return this;
    }

    public CommandBuilder AddAliases(IEnumerable<string> aliases)
    {
        this.aliases.AddRange(aliases);

        return this;
    }

    public CommandBuilder AddExecutionCheck(BaseExecutionCheckAttribute check)
    {
        this.checks.Add(check);

        return this;
    }

    public CommandBuilder AddExecutionChecks(params BaseExecutionCheckAttribute[] checks)
    {
        this.checks.AddRange(checks);

        return this;
    }

    public CommandBuilder AddExecutionChecks(IEnumerable<BaseExecutionCheckAttribute> checks)
    {
        this.checks.AddRange(checks);

        return this;
    }


    public CommandBuilder WithUsage(string? usage)
    {
        this.Usage = usage;

        return this;
    }

    public CommandBuilder CanIssueAs(CommandIssuers issuers)
    {
        this.Issuers |= issuers;

        return this;
    }

    public Command Build(CommandHandler commandHandler, IPluginContainer? pluginContainer) => new()
    {
        Name = this.Name,
        Aliases = this.aliases.ToArray(),
        Description = this.Description,
        Usage = this.Usage,
        Overloads = new(this.overloads),
        AllowedIssuers = this.Issuers,
        Parent = this.Parent,
        ExecutionChecks = this.checks.ToArray(),
        CommandHandler = commandHandler,
        PluginContainer = pluginContainer,
    };
}
