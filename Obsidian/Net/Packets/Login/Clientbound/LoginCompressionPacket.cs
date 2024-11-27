using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login.Clientbound;

public partial class LoginCompressionPacket(int threshold)
{
    [Field(0), VarLength]
    public int Threshold { get; } = threshold;
    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(Threshold);
    }
}
