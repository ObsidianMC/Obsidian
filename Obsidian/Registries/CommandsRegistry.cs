using Obsidian.Commands;
using Obsidian.Commands.Parsers;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Registries;
public static class CommandsRegistry
{
    internal static CommandsPacket Packet = new();

    public static void Register(Server server)
    {
        Packet = new();
        var index = 0;

        var node = new CommandNode()
        {
            Type = CommandNodeType.Root,
            Index = index
        };

        foreach (var cmd in server.CommandsHandler.GetAllCommands())
        {
            var cmdNode = new CommandNode()
            {
                Index = ++index,
                Name = cmd.Name,
                Type = CommandNodeType.Literal
            };

            foreach (var overload in cmd.Overloads)
            {
                var args = overload.GetParameters();
                if (!args.Any())
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
                        _ => new CommandParser(id, mctype),
                    };

                    prev.AddChild(argNode);

                    prev = argNode;
                }
            }

            node.AddChild(cmdNode);
        }

        Packet.AddNode(node);
    }
}
