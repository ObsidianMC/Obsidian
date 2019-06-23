using Obsidian.Commands;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    /// <summary>
    /// https://wiki.vg/index.php?title=Protocol#Declare_Commands
    /// </summary>
    public class DeclareCommands : Packet
    {
        public CommandNode RootNode;

        public List<CommandNode> Nodes { get; } = new List<CommandNode>();

        public DeclareCommands() : base(0x11, new byte[0])
        {
            this.RootNode = new CommandNode()
            {
                Type = CommandNodeType.Root,
                Owner = this,
            };
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var ms = new MinecraftStream())
            {
                await ms.WriteVarIntAsync(this.Nodes.Count);

                foreach (CommandNode node in Nodes)
                {
                    await ms.WriteAsync(await node.ToArrayAsync());
                }

                //Constant root node index
                await ms.WriteVarIntAsync(0);

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Adds a node to this packet, it is UNRECOMMENDED to use <see cref="DeclareCommands.Nodes.Add()"/>, since it's badly implemented.
        /// </summary>
        /// <param name="node"></param>
        public void AddNode(CommandNode node)
        {
            node.Owner = this;
            Nodes.Add(node);

            foreach (var childs in node.Children)
            {
                AddNode(childs);
            }
        }

        public override Task PopulateAsync() => throw new NotImplementedException();
    }
}