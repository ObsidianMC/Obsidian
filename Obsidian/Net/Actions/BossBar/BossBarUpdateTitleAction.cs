namespace Obsidian.Net.Actions.BossBar;

public sealed class BossBarUpdateTitleAction : BossBarAction
{
    public required ChatMessage Title { get; set; }

    public BossBarUpdateTitleAction() : base(3) { }

    public override void WriteTo(INetStreamWriter writer)
    {
        base.WriteTo(writer);

        writer.WriteChat(Title);
    }
}
