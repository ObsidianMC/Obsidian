using Obsidian.Commands;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

// Source: https://wiki.vg/index.php?title=Protocol#Declare_Commands
public partial class DeclareCommands : IClientboundPacket
{
    [Field(0)]
    public List<CommandNode> Nodes { get; } = new();

    [Field(1), VarLength]
    public int RootIndex { get; }

    public int Id => 0x12;
    public void Serialize(MinecraftStream stream) => throw new NotImplementedException();

    public void AddNode(CommandNode node)
    {
        Nodes.Add(node);

        foreach (var child in node.Children)
            AddNode(child);
    }
}
