using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Handshake.Serverbound;

public partial class IntentionPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public ProtocolVersion Version { get; private set; }

    [Field(1)]
    public string ServerAddress { get; private set; }

    [Field(2)]
    public ushort ServerPort { get; private set; }

    [Field(3), ActualType(typeof(int)), VarLength]
    public ClientState NextState { get; private set; }

    public static IntentionPacket Deserialize(byte[] data)
    {
        var packet = new IntentionPacket();

        packet.Populate(data);

        return packet;
    }
}
