namespace Obsidian.Net.Actions.BossBar;

public sealed class BossBarUpdateHealthAction : BossBarAction
{
    public float Health { get; set; }

    public BossBarUpdateHealthAction() : base(2) { }

    public override void WriteTo(INetStreamWriter writer)
    {
        base.WriteTo(writer);

        writer.WriteSingle(Health);
    }
}
