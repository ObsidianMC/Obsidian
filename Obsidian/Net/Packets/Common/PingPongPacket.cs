using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Common;

// Like vanilla's ClientboundPingPacket and ServerboundPongPacket, the id is a plain int.
public partial record class PingPacket
{
    [Field(0)]
    public int IdValue { get; set; }

    public override void Populate(INetStreamReader reader) => IdValue = reader.ReadInt();
    public override void Serialize(INetStreamWriter writer) => writer.WriteInt(IdValue);
}

public partial record class PongPacket
{
    [Field(0)]
    public int IdValue { get; set; }

    public override void Populate(INetStreamReader reader) => IdValue = reader.ReadInt();
    public override void Serialize(INetStreamWriter writer) => writer.WriteInt(IdValue);
}
