using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetTitleTextPacket
{
    [Field(0)]
    public required ChatMessage Text { get; init; }

    public SetTitleTextPacket(TitleMode mode)
    {
        this.Id = mode == TitleMode.SetTitle ? 0x65 : 0x63;
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
}

public enum TitleMode
{
    SetTitle,

    SetSubtitle
}
