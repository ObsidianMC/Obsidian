namespace Obsidian.Net.Actions.PlayerInfo;
public sealed class UpdateListedInfoAction : InfoAction
{
    public override PlayerInfoAction Type => PlayerInfoAction.UpdateListed;

    public bool Listed { get; init; }

    public UpdateListedInfoAction(bool listed) => this.Listed = listed;

    public override void Write(MinecraftStream stream)
    {
        stream.WriteBoolean(this.Listed);
    }

    public override async Task WriteAsync(MinecraftStream stream) => await stream.WriteBooleanAsync(this.Listed);
}
