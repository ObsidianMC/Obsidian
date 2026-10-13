using Microsoft.Extensions.Logging;
using Obsidian.API.Commands;
using Obsidian.API.Commands.ArgumentParsers;
using Obsidian.API.Utilities.Interfaces;
using System.Reflection;

namespace Obsidian.Commands.Framework;

public sealed partial class CommandHandler
{
    private const string AskServerSuggestions = "minecraft:ask_server";

    /// <summary>
    /// Builds the command tree a client completes, highlights and validates commands with, as vanilla's
    /// <c>Commands.sendCommands</c> does for each player.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Only commands and overloads the sender may run are included, so clients don't offer the rest.</item>
    /// <item>Overloads share nodes for leading parameters that read the same way, and only nodes where an overload
    /// ends are executable, so the client reports incomplete commands.</item>
    /// <item>Aliases redirect to their command's node, so they complete like the command.</item>
    /// <item>Each argument is written with its registered parser, so the client knows its type and bounds, and asks
    /// the server for suggestions if the parameter or parser provides them.</item>
    /// </list>
    /// </remarks>
    /// <param name="ctx">The sender the tree is for. Its message is unused.</param>
    /// <returns>The root node.</returns>
    internal async Task<CommandNode> BuildCommandTreeAsync(CommandContext ctx)
    {
        var commands = GetAllCommands();
        var root = new CommandNode { Type = CommandNodeType.Root };

        await AddCommandNodesAsync(root, null, commands, ctx);

        return root;
    }

    private async Task AddCommandNodesAsync(CommandNode parentNode, Command? parent, Command[] commands, CommandContext ctx)
    {
        var added = new List<(Command Command, CommandNode Node)>();

        foreach (var command in commands.Where(x => x.Parent == parent))
        {
            // Brigadier keys children by name, so a second command of the same name would be merged into the first.
            if (parentNode.Children.Any(x => x.Name == command.Name) || !await CanSuggestNameAsync(command, commands, ctx))
                continue;

            var node = new CommandNode { Name = command.Name, Type = CommandNodeType.Literal };

            // Subcommands first, so that an argument named like one is renamed rather than hiding it.
            await AddCommandNodesAsync(node, command, commands, ctx);

            foreach (var overload in command.Overloads)
            {
                if (await command.FindFailedCheckAsync(overload, ctx) is null)
                    AddOverloadNodes(node, overload);
            }

            // Nothing under it can be run, such as a group whose subcommands are all denied.
            if (node.Children.Count == 0 && !node.Type.HasFlag(CommandNodeType.IsExecutable))
                continue;

            parentNode.AddChild(node);
            added.Add((command, node));
        }

        // Aliases come after every command, so that a command's name wins over another's alias.
        foreach (var (command, node) in added)
        {
            foreach (var alias in command.Aliases.Distinct())
            {
                if (parentNode.Children.Any(x => x.Name == alias))
                    continue;

                parentNode.AddChild(new CommandNode
                {
                    Name = alias,
                    Type = CommandNodeType.Literal | (node.Type & CommandNodeType.IsExecutable),
                    // Like vanilla's aliases, parsing continues with the command's arguments and subcommands.
                    Redirect = node.Children.Count > 0 ? node : null
                });
            }
        }
    }

    /// <summary>
    /// Adds a path of argument nodes for the parameters of <paramref name="overload"/> under its command's node,
    /// reusing the nodes of earlier overloads for leading parameters that read the same way.
    /// </summary>
    private void AddOverloadNodes(CommandNode commandNode, IExecutor<CommandContext> overload)
    {
        var parameters = overload.GetParameters();

        // An overload with a parameter that can't be read can't run, so the client shouldn't offer it.
        if (parameters.Any(x => !IsValidArgumentType(x.ParameterType)))
        {
            Log.OverloadWithoutParser(this.logger, overload.ToString());
            return;
        }

        var current = commandNode;

        foreach (var parameter in parameters)
            current = GetOrAddArgumentNode(current, parameter);

        current.Type |= CommandNodeType.IsExecutable;
    }

    private CommandNode GetOrAddArgumentNode(CommandNode parent, ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        var greedy = parameter.GetCustomAttribute<RemainingAttribute>() is not null;
        var asksServer = FindSuggestionProvider(parameter) is not null;
        var key = (type, greedy, asksServer);

        var parser = GetArgumentParser(type);

        // A [Remaining] string takes the rest of the line, as it does when the command runs. Other parsers, such as
        // vec3, already read as many words as they need.
        if (greedy && parser is StringArgumentParser)
            parser = new StringArgumentParser(StringType.GreedyPhrase);

        var name = parameter.Name ?? "value";

        // Brigadier keys children by name, so a parameter reading differently from a sibling of the same name, as
        // in overloads Set(string value) and Set(int value), needs a name of its own to keep its type.
        for (var attempt = 1; ; attempt++)
        {
            var sibling = parent.Children.FirstOrDefault(x => x.Name == name);

            if (sibling is null)
                break;

            if (sibling.Type.HasFlag(CommandNodeType.Argument) && Equals(sibling.MergeKey, key))
                return sibling;

            var baseName = $"{parameter.Name ?? "value"}_{parser.Identifier[(parser.Identifier.IndexOf(':') + 1)..]}";
            name = attempt == 1 ? baseName : $"{baseName}_{attempt}";
        }

        var node = new CommandNode
        {
            Name = name,
            Type = CommandNodeType.Argument,
            Parser = parser,
            MergeKey = key
        };

        if (asksServer)
        {
            // The client sends a CommandSuggestionPacket for nodes with vanilla's ask_server provider.
            node.Type |= CommandNodeType.HasSuggestions;
            node.SuggestionType = AskServerSuggestions;
        }

        parent.AddChild(node);

        return node;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Leaving {Overload} out of the command tree: a parameter type has no argument parser.")]
        public static partial void OverloadWithoutParser(ILogger logger, string? overload);
    }
}
