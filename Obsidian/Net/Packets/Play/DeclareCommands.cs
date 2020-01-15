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

        public DeclareCommands() : base(0x11, Array.Empty<byte>())
        {
            this.RootNode = new CommandNode()
            {
                Type = CommandNodeType.Root,
                Owner = this,
            };
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
                AddNode(childs);
        }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteVarIntAsync(this.Nodes.Count);

            foreach (var node in Nodes)
                await stream.WriteAsync(await node.ToArrayAsync());

            //Constant root node index
            await stream.WriteVarIntAsync(0);
        }

        protected override Task PopulateAsync(MinecraftStream stream) => throw new NotImplementedException();
    }
}