using Obsidian.Commands;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    /// <summary>
    /// https://wiki.vg/index.php?title=Protocol#Declare_Commands
    /// </summary>
    public class DeclareCommands : IPacket
    {
        [Field(0, Type = DataType.VarInt)]
        public int NodeCount => this.Nodes.Count;

        [Field(1, Type = DataType.Array)]
        public List<CommandNode> Nodes { get; } = new List<CommandNode>();

        public int Id => 0x10;

        public byte[] Data { get; }

        [Field(2, Type = DataType.VarInt)]
        public int RootIndex = 0;

        public DeclareCommands() { }

        public void AddNode(CommandNode node)
        {
            this.Nodes.Add(node);

            foreach (var child in node.Children)
                this.AddNode(child);
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}