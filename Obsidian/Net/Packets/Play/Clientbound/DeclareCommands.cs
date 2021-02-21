using Obsidian.Commands;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    // Source: https://wiki.vg/index.php?title=Protocol#Declare_Commands
    [ClientOnly]
    public partial class DeclareCommands : ISerializablePacket
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

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}