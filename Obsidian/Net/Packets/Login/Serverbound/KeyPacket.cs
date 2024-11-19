using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login.Serverbound;

public partial class KeyPacket
{
    [Field(0)]
    public byte[] SharedSecret { get; private set; }

    [Field(1)]
    public byte[] VerifyToken { get; private set; }
}
