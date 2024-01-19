﻿using Microsoft.Extensions.DependencyInjection;
using Obsidian.API.BlockStates;
using Obsidian.API.Utilities;
using Obsidian.Commands.Framework.Exceptions;
using Obsidian.Plugins;
using System;
using System.Reflection;

namespace Obsidian.Commands.Framework.Entities;

public sealed class Command
{
    internal CommandIssuers AllowedIssuers { get; init; }

    private bool HasModule => this.ModuleType != null;

    public required Type? ModuleType { get; init; }

    public required CommandHandler CommandHandler { get; init; }
    public required PluginContainer? PluginContainer { get; init; }
    public required string Name { get; init; }

    public string[] Aliases { get; init; } = [];
    public string? Description { get; init; }
    public string? Usage { get; init; }

    public List<MethodInfo> Overloads { get; init; } = [];
    public BaseExecutionCheckAttribute[] ExecutionChecks { get; init; } = [];

    public Command? Parent { get; init; }

    public ObjectFactory? ModuleFactory { get; init; }

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

        var method = Overloads.FirstOrDefault(x => this.MatchParams(x, args)
            || x.GetParameters().Last().GetCustomAttribute<RemainingAttribute>() != null);

        // Find matching overload
        if (method == null)
        {
            //throw new InvalidCommandOverloadException($"No such overload for command {this.GetQualifiedName()}");
            await context.Sender.SendMessageAsync($"&4Correct usage: {Usage}");

            return;
        }

        await this.ExecuteAsync(method, context, args);
    }

    private bool MatchParams(MethodInfo method, string[] args) =>
        this.HasModule ? method.GetParameters().Length == args.Length : method.GetParameters().Length - 1 == args.Length;

    private async Task ExecuteAsync(MethodInfo method, CommandContext context, string[] args)
    {
        using var serviceScope = this.CommandHandler.ServiceProvider.CreateScope();

        object? module = this.PluginContainer != null
            ? this.HasModule ? CommandModuleFactory.CreateModule(this.ModuleFactory!, context, this.PluginContainer) : null
            : this.HasModule ? CommandModuleFactory.CreateModule(this.ModuleFactory!, context, serviceScope.ServiceProvider) : null;

        var methodparams = method.GetParameters().ToArray();
        var parsedargs = new object[methodparams.Length];

        // Set first parameter to be the context if there isn't a module.
        if (!this.HasModule)
            parsedargs[0] = context;

        for (int i = this.HasModule ? 0 : 1; i < methodparams.Length; i++)
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
        var checks = method.GetCustomAttributes<BaseExecutionCheckAttribute>();

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
        object? result = method.Invoke(module, parsedargs);
        if (result is Task task)
        {
            await task.ConfigureAwait(false);
        }
        else if (result is ValueTask valueTask)
        {
            await valueTask;
        }
    }

    public override string ToString() => $"{CommandHelpers.DefaultPrefix}{GetQualifiedName()}";
}
