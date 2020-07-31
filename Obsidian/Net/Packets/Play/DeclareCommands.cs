using Obsidian.Commands;
using Obsidian.Serializer.Attributes;
using System.Collections.Generic;

namespace Obsidian.Net.Packets.Play
{
    /// <summary>
    /// https://wiki.vg/index.php?title=Protocol#Declare_Commands
    /// </summary>
    public class DeclareCommands : Packet
    {
        [PacketOrder(0)]
        public List<CommandNode> Nodes { get; } = new List<CommandNode>();

        public DeclareCommands() : base(0x11) { }

        /// <summary>
        /// Adds a node to this packet, it is UNRECOMMENDED to use <see cref="DeclareCommands.Nodes.Add()"/>, since it's badly implemented.
        /// </summary>
        public void AddNode(CommandNode node)
        {
            node.Owner = this;
            Nodes.Add(node);

            foreach (var childs in node.Children)
                AddNode(childs);
        }
    }
}