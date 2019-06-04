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
        public DeclareCommands() : base(0x11, new byte[0])
        {
            this.RootIndex = 0;
            this.Nodes.Add(new CommandNode()
            {
                Type = CommandNodeType.Root
            });
        }

        public int Count => Nodes.Count;
        public List<CommandNode> Nodes { get; set; } = new List<CommandNode>();

        public int RootIndex;

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var ms = new MinecraftStream())
            {
                await ms.WriteVarIntAsync(this.Count);

                foreach (CommandNode node in Nodes)
                {
                    await ms.WriteAsync(await node.ToArrayAsync());
                }

                await ms.WriteVarIntAsync(this.RootIndex);

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Adds a node to this packet, it is UNRECOMMENDED to use <see cref="DeclareCommands.Nodes.Add()"/>, since it's badly implemented.
        /// </summary>
        /// <param name="node"></param>
        public void AddNode(CommandNode node)
        {
            Nodes.Add(node);

            int startIndex = Nodes.Count;
            int index = startIndex;

            foreach (CommandNode childNode in node.Children)
            {
                Nodes.Add(childNode);
                node.ChildrenIndices.Add(index);
                index++;
            }

        }

        protected override Task PopulateAsync() => throw new NotImplementedException();
    }
}
