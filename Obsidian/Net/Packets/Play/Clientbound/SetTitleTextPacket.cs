using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetTitleTextPacket
{
    [Field(0)]
    public required ChatMessage Text { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteChat(this.Text);
    }
}

public partial class SetSubtitleTextPacket
{
    [Field(0)]
    public required ChatMessage Text { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteChat(this.Text);
    }
}

public partial class SetTitlesAnimationPacket
{
    [Field(0)]
    public int FadeIn { get; set; }

    [Field(1)]
    public int Stay { get; set; }

    [Field(2)]
    public int FadeOut { get; set; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteInt(this.FadeIn);
        writer.WriteInt(this.Stay);
        writer.WriteInt(this.FadeOut);
    }
}
