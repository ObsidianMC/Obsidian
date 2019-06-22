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
        public List<CommandNode> Nodes { get; set; } = new List<CommandNode>();

        public int RootIndex;

        public DeclareCommands() : base(0x11, new byte[0])
        {
            this.RootIndex = 0;
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

            foreach (var childs in node.Children)
            {
                Nodes.Add(childs);
            }
        }

        public override Task PopulateAsync() => throw new NotImplementedException();
    }
}
