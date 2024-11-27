using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetActionBarTextPacket
{
    [Field(0)]
    public required ChatMessage Text { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteChat(this.Text);
    }
}
