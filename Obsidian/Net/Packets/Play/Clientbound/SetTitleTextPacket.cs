using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetTitleTextPacket : IClientboundPacket
{
    [Field(0)]
    public ChatMessage Text { get; init; }

    public int Id { get; }

    public SetTitleTextPacket(TitleMode mode)
    {
        this.Id = mode == TitleMode.SetTitle ? 0x5F : 0x5D;
    }
}

public partial class SetTitleAnimationTimesPacket : IClientboundPacket
{
    [Field(0)]
    public int FadeIn { get; set; }

    [Field(1)]
    public int Stay { get; set; }

    [Field(2)]
    public int FadeOut { get; set; }

    public int Id => 0X60;
}

public enum TitleMode
{
    SetTitle,

    SetSubtitle
}
