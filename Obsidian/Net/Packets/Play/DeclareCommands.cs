using Obsidian.Commands;
using Obsidian.Util;
using System.Collections.Generic;

namespace Obsidian.Net.Packets
{
    /// <summary>
    /// https://wiki.vg/index.php?title=Protocol#Declare_Commands
    /// </summary>
    public class DeclareCommands : Packet
    {
        public CommandNode RootNode;

        [Variable]
        public List<CommandNode> Nodes { get; } = new List<CommandNode>();

        [Variable]
        public int RootNodeIndex = 0;

        public DeclareCommands() : base(0x11, new byte[0])
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
            {
                AddNode(childs);
            }
        }
    }
}