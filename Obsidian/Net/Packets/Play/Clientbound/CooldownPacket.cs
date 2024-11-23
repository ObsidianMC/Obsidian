using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class CooldownPacket
{
    [Field(0)]
    public required string CooldownGroup { get; init; }

    [Field(1), VarLength]
    public required int CooldownTicks { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(this.CooldownGroup);
        writer.WriteVarInt(this.CooldownTicks);
    }
}
