using Obsidian.API.Boss;

namespace Obsidian.Net.Actions.BossBar;

public sealed class BossBarAddAction : BossBarAction
{
    public required ChatMessage Title { get; set; }

    public float Health { get; set; }

    public BossBarColor Color { get; set; }

    public BossBarDivisionType Division { get; set; }

    public BossBarFlags Flags { get; set; }

    public BossBarAddAction() : base(0) { }

    public override void WriteTo(INetStreamWriter writer)
    {
        base.WriteTo(writer);

        writer.WriteChat(Title);
        writer.WriteFloat(Health);
        writer.WriteVarInt(Color);
        writer.WriteVarInt(Division);
        writer.WriteByte((byte)Flags);
    }
}
