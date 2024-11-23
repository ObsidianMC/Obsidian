using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login.Serverbound;

public partial class KeyPacket
{
    [Field(0)]
    public byte[] SharedSecret { get; private set; } = default!;

    [Field(1)]
    public byte[] VerifyToken { get; private set; } = default!;

    public override void Populate(INetStreamReader reader)
    {
        this.SharedSecret = reader.ReadByteArray();
        this.VerifyToken = reader.ReadByteArray();
    }
}
