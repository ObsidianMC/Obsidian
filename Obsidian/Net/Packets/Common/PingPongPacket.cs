using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Common;

public partial record class PingPacket
{
    [Field(0)]
    public long Payload { get; set; }

    public override void Populate(INetStreamReader reader) => Payload = reader.ReadLong();
    public override void Serialize(INetStreamWriter writer) => writer.WriteLong(Payload);
}

public partial record class PongPacket
{
    [Field(0)]
    public long Payload { get; set; }

    public override void Populate(INetStreamReader reader) => Payload = reader.ReadLong();
    public override void Serialize(INetStreamWriter writer) => writer.WriteLong(Payload);
}
