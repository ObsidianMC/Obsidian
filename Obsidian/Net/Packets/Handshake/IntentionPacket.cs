using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Handshake.Serverbound;

public partial class IntentionPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public ProtocolVersion Version { get; private set; }

    [Field(1)]
    public string ServerAddress { get; private set; } = default!;

    [Field(2)]
    public ushort ServerPort { get; private set; }

    [Field(3), ActualType(typeof(int)), VarLength]
    public ClientState NextState { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Version = (ProtocolVersion)reader.ReadVarInt();
        this.ServerAddress = reader.ReadString();
        this.ServerPort = reader.ReadUnsignedShort();
        this.NextState = (ClientState)reader.ReadVarInt();
    }
}
