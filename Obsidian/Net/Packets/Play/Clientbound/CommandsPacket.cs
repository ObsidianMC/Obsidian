using Obsidian.Commands;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

// Source: https://wiki.vg/index.php?title=Protocol#Declare_Commands
public partial class CommandsPacket
{
    [Field(0)]
    public List<CommandNode> Nodes { get; } = new();

    [Field(1), VarLength]
    public int RootIndex { get; }

    public void AddNode(CommandNode node)
    {
        Nodes.Add(node);

        foreach (var child in node.Children)
            AddNode(child);
    }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.Nodes.Count);

        foreach (var child in this.Nodes)
            child.CopyTo(writer);

        writer.WriteVarInt(this.RootIndex);
    }
}
