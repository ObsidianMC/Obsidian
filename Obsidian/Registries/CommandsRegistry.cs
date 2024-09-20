using Microsoft.Extensions.Logging;
using Obsidian.Commands;
using Obsidian.Commands.Parsers;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Xml;
using System;
using Obsidian.Utilities.Interfaces;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Obsidian.Commands.Framework.Entities;
using System.Reflection;

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
            Register(server, cmd, commands, rootNode, ref index);
        }

        Packet.AddNode(rootNode);
    }

    private static void Register(Server server, Command cmd, IEnumerable<Command> commands, CommandNode node, ref int index)
    {
        if (cmd.Parent != null)//Don't register commands alone that have parents 
            return;

        var cmdNode = new CommandNode()
        {
            Index = ++index,
            Name = cmd.Name,
            Type = CommandNodeType.Literal
        };

        foreach (var overload in cmd.Overloads)
        {
            RegisterChildNodeOverload(server, overload, cmdNode, ref index);
        }

        //Register children commands for groups
        foreach (var childrenCommand in commands.Where(x => x.Parent == cmd))
        {
            var nodeType = childrenCommand.Overloads.Any(x => x.GetParameters().Length == 0) ? CommandNodeType.IsExecutable | CommandNodeType.Literal : CommandNodeType.Literal;
            var childCmdNode = new CommandNode()
            {
                Index = ++index,
                Name = childrenCommand.Name,
                Type = nodeType
            };

            foreach (var overload in childrenCommand.Overloads)
            {
                RegisterChildNodeOverload(server, overload, childCmdNode, ref index);
            }

            cmdNode.AddChild(childCmdNode);
        }


        node.AddChild(cmdNode);
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
