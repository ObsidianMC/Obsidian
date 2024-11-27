using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetDisplayObjectivePacket
{
    [Field(0), VarLength]
    public required DisplaySlot DisplaySlot { get; init; }

    [Field(1), FixedLength(16)]
    public required string ObjectiveName { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.DisplaySlot);
        writer.WriteString(this.ObjectiveName);
    }
}
