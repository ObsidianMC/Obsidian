using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class TitlePacket : IClientboundPacket
{
    [Field(0)]
    public ChatMessage Text { get; set; }

    public int Id { get; }
    public void Serialize(MinecraftStream stream) => throw new NotImplementedException();

    public TitlePacket(TitleMode mode)
    {
        Id = mode == TitleMode.SetTitle ? 0x5A : 0x58;
    }
}

public partial class TitleTimesPacket : IClientboundPacket
{
    [Field(0)]
    public int FadeIn { get; set; }

    [Field(1)]
    public int Stay { get; set; }

    [Field(2)]
    public int FadeOut { get; set; }

    public int Id => 0X5B;
    public void Serialize(MinecraftStream stream) => throw new NotImplementedException();
}

public enum TitleMode
{
    SetTitle,

    SetSubtitle
}
