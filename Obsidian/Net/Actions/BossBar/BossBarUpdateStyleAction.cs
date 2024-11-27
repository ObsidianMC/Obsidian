using Obsidian.API.Boss;

namespace Obsidian.Net.Actions.BossBar;

public sealed class BossBarUpdateStyleAction : BossBarAction
{
    public BossBarColor Color { get; set; }
    public BossBarDivisionType Division { get; set; }

    public BossBarUpdateStyleAction() : base(4) { }

    public override void WriteTo(INetStreamWriter writer)
    {
        base.WriteTo(writer);

        writer.WriteVarInt(Color);
        writer.WriteVarInt(Division);
    }
}
