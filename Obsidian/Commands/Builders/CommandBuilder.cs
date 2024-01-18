﻿using Microsoft.Extensions.DependencyInjection;
using Obsidian.API.BlockStates;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Framework.Entities;
using Obsidian.Plugins;
using System.Reflection;

namespace Obsidian.Commands.Builders;
public sealed class CommandBuilder
{
    private readonly List<string> aliases = [];
    private readonly List<MethodInfo> overloads = [];
    private readonly List<BaseExecutionCheckAttribute> checks = [];

    public string Name { get; }

    public Type? ModuleType { get; }

    public string? Description { get; private set; } = default!;

    public string? Usage { get; private set; } = default!;

    public Command? Parent { get; private set; } = default!;

    public CommandIssuers Issuers { get; private set; }

    public IReadOnlyCollection<MethodInfo> Overloads => this.overloads.AsReadOnly();
    public IReadOnlyCollection<string> Aliases => this.aliases.AsReadOnly();
    public IReadOnlyCollection<BaseExecutionCheckAttribute> Checks => this.checks.AsReadOnly();

    private CommandBuilder(string name, Type moduleType)
    {
        this.Name = name;
        this.ModuleType = moduleType;
    }

    private CommandBuilder(string name)
    {
        this.Name = name;
    }

    public static CommandBuilder Create(string name, Type moduleType) => new(name, moduleType);

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

    public CommandBuilder AddOverload(MethodInfo methodInfo)
    {
        this.overloads.Add(methodInfo);

        return this;
    }

    public CommandBuilder AddOverloads(params MethodInfo[] methods)
    {
        this.overloads.AddRange(methods);

        return this;
    }

    public CommandBuilder AddOverloads(IEnumerable<MethodInfo> methods)
    {
        this.overloads.AddRange(methods);

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

    public Command Build(CommandHandler commandHandler, PluginContainer pluginContainer) => new()
    {
        Name = this.Name,
        Aliases = this.aliases.ToArray(),
        Description = this.Description,
        Usage = this.Usage,
        Overloads = new(this.overloads),
        AllowedIssuers = this.Issuers,
        Parent = this.Parent,
        ExecutionChecks = this.checks.ToArray(),
        ModuleType = this.ModuleType,
        CommandHandler = commandHandler,
        PluginContainer = pluginContainer,
        ModuleFactory = this.ModuleType != null ? ActivatorUtilities.CreateFactory(this.ModuleType, []) : null
    };
}
