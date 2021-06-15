using Obsidian.Commands;
using Obsidian.Serialization.Attributes;
using System.Collections.Generic;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    // Source: https://wiki.vg/index.php?title=Protocol#Declare_Commands
    public partial class DeclareCommands : IClientboundPacket
    {
        [Field(0)]
        public List<CommandNode> Nodes { get; } = new();

        [Field(1), VarLength]
        public int RootIndex = 0;

        public int Id => 0x10;

        public void AddNode(CommandNode node)
        {
            Nodes.Add(node);

            foreach (var child in node.Children)
                AddNode(child);
        }
    }
}