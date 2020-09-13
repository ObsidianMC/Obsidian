using Obsidian.Commands;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Collections.Generic;

namespace Obsidian.Net.Packets.Play.Client
{
    /// <summary>
    /// https://wiki.vg/index.php?title=Protocol#Declare_Commands
    /// </summary>
    public class DeclareCommands : Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public int NodeCount => this.Nodes.Count;

        [Field(1, Type = DataType.Array)]
        public List<CommandNode> Nodes { get; } = new List<CommandNode>();

        [Field(2, Type = DataType.VarInt)]
        public int RootIndex = 0;

        public DeclareCommands() : base(0x12) { }

        public void AddNode(CommandNode node)
        {
            this.Nodes.Add(node);

            foreach (var child in node.Children)
                this.AddNode(child);
        }
    }
}