using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login;

public partial class SetCompression : IClientboundPacket
{
    [Field(0)]
    public int Threshold { get; }

    public bool Enabled => Threshold < 0;

    public int Id => 0x03;

    public SetCompression(int threshold)
    {
        Threshold = threshold;
    }
}
