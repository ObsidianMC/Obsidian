namespace Obsidian.Net.Actions.PlayerInfo;
public sealed class UpdateListedInfoAction(bool listed) : InfoAction
{
    public override PlayerInfoAction Type => PlayerInfoAction.UpdateListed;

    public bool Listed { get; init; } = listed;

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteBoolean(this.Listed);
    }
}
