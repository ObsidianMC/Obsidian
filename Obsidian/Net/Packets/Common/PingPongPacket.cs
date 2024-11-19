using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Common;

public partial record class PingPacket
{
    [Field(0)]
    public long Payload { get; set; }

    public static PingPacket Deserialize(byte[] data)
    {
        var packet = new PingPacket();

        packet.Populate(data);

        return packet;
    }
}

public partial record class PongPacket
{
    [Field(0)]
    public long Payload { get; set; }

    public static PongPacket Deserialize(byte[] data)
    {
        var packet = new PongPacket();

        packet.Populate(data);

        return packet;
    }
}
