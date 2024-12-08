using Obsidian.Commands;
using Obsidian.Commands.Framework.Entities;
using Obsidian.Commands.Parsers;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Utilities.Interfaces;

namespace Obsidian.Registries;
public static class CommandsRegistry
{
    internal static CommandsPacket Packet = new();

    public static void Register(Server server)
    {
        Packet = new();
        var index = 0;
        var commands = server.CommandsHandler.GetAllCommands()!;

        var rootNode = new CommandNode()
        {
            Type = CommandNodeType.Root,
            Index = index
        };

        foreach (var cmd in commands)
        {
            //Don't register commands alone that have parents 
            //This is very inconvenient and should be changed
            if (cmd.Parent != null)
                continue;

            Register(server, cmd, commands, rootNode, ref index);
        }

        Packet.AddNode(rootNode);
    }

    private static void Register(Server server, Command command, IEnumerable<Command> commands, CommandNode node, ref int index)
    {
        var nodeType = command.Overloads.Any(x => x.GetParameters().Length == 0) ? CommandNodeType.IsExecutable | CommandNodeType.Literal : CommandNodeType.Literal;
        var parentNode = new CommandNode()
        {
            Index = ++index,
            Name = command.Name,
            Type = nodeType
        };

        foreach (var overload in command.Overloads)
            RegisterChildNodeOverload(server, overload, parentNode, ref index);

        foreach (var childrenCommand in commands.Where(x => x.Parent == command))
            Register(server, childrenCommand, commands, parentNode, ref index);

        node.AddChild(parentNode);
    }

    private static void RegisterChildNodeOverload(Server server, IExecutor<CommandContext> overload, CommandNode cmdNode, ref int index)
    {
        var args = overload.GetParameters();
        if (args.Length == 0)
            cmdNode.Type |= CommandNodeType.IsExecutable;

        var prev = cmdNode;

        foreach (var arg in args)
        {
            var argNode = new CommandNode()
            {
                Index = ++index,
                Name = arg.Name,
                Type = CommandNodeType.Argument | CommandNodeType.IsExecutable
            };

            var type = arg.ParameterType;

            var (id, mctype) = server.CommandsHandler.FindMinecraftType(type);

            //TODO make this better 
            argNode.Parser = mctype switch
            {
                "brigadier:string" => new StringCommandParser(arg.CustomAttributes.Any(x => x.AttributeType == typeof(RemainingAttribute)) ? StringType.GreedyPhrase : StringType.QuotablePhrase),
                "brigadier:double" => new DoubleCommandParser(),
                "brigadier:float" => new FloatCommandParser(),
                "brigadier:integer" => new IntCommandParser(),
                "brigadier:long" => new LongCommandParser(),
                "minecraft:time" => new MinecraftTimeParser(),
                _ => new CommandParser(id, mctype),
            };

            prev.AddChild(argNode);

            prev = argNode;
        }
    }
}
