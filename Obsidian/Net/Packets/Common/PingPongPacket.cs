using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Common;

public partial record class PingPacket
{
    [Field(0)]
    public long Payload { get; set; }
}

public partial record class PongPacket
{
    [Field(0)]
    public long Payload { get; set; }
}
