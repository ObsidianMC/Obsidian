using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login.Clientbound;

public partial class LoginCompressionPacket
{
    [Field(0)]
    public int Threshold { get; }

    public bool Enabled => Threshold < 0;

    public LoginCompressionPacket(int threshold)
    {
        Threshold = threshold;
    }
}
