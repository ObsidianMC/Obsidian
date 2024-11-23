using Obsidian.API.Boss;

namespace Obsidian.Net.Actions.BossBar;

public sealed class BossBarUpdateFlagsAction : BossBarAction
{
    public BossBarFlags Flags { get; set; }

    public BossBarUpdateFlagsAction() : base(5) { }

    public override void WriteTo(INetStreamWriter writer)
    {
        base.WriteTo(writer);

        writer.WriteByte((byte)Flags);
    }
}
