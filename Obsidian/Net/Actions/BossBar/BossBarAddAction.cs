using Obsidian.API;
using Obsidian.API.Boss;
using System.Threading.Tasks;

namespace Obsidian.Net.Actions.BossBar;

public class BossBarAddAction : BossBarAction
{
    public ChatMessage Title { get; set; }

    public float Health { get; set; }

    public BossBarColor Color { get; set; }

    public BossBarDivisionType Division { get; set; }

    public BossBarFlags Flags { get; set; }

    public BossBarAddAction() : base(0) { }

    public override void WriteTo(MinecraftStream stream)
    {
        base.WriteTo(stream);

        stream.WriteChat(Title);
        stream.WriteFloat(Health);
        stream.WriteVarInt(Color);
        stream.WriteVarInt(Division);
        stream.WriteUnsignedByte((byte)Flags);
    }
    public override async Task WriteToAsync(MinecraftStream stream)
    {
        await base.WriteToAsync(stream);

        await stream.WriteChatAsync(Title);
        await stream.WriteFloatAsync(Health);
        await stream.WriteVarIntAsync(Color);
        await stream.WriteVarIntAsync(Division);
        await stream.WriteUnsignedByteAsync((byte)Flags);
    }
}
