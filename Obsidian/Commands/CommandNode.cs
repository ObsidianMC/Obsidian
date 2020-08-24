using Obsidian.Net;
using Obsidian.Net.Packets;
using System.Collections.Generic;
using System.Threading.Tasks;
using Obsidian.Net.Packets.Play;

namespace Obsidian.Commands
{
    /// <summary>
    /// https://wiki.vg/Command_Data
    /// </summary>
    public class CommandNode
    {
        public CommandNodeType Type;

        public List<CommandNode> Children = new List<CommandNode>();

        public string Name;

        public DeclareCommands Owner;

        public int Index => Owner.Nodes.IndexOf(this);

        public CommandParser Parser;

        public async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteByteAsync((sbyte)Type);
                await stream.WriteVarIntAsync(Children.Count);

                foreach (CommandNode childNode in Children)
                {
                    await stream.WriteVarIntAsync(childNode.Index);
                }

                if (Type.HasFlag(CommandNodeType.HasRedirect))
                {
                    //TODO: Add redirect functionality if needed
                    await stream.WriteVarIntAsync(0);
                }

                if (Type.HasFlag(CommandNodeType.Argument) || Type.HasFlag(CommandNodeType.Literal))
                {
                    if (!string.IsNullOrWhiteSpace(Name))
                        await stream.WriteStringAsync(Name);
                }

                if (Type.HasFlag(CommandNodeType.Argument))
                {
                    await Parser.WriteAsync(stream);
                }

                return stream.ToArray();
            }
        }
    }
}