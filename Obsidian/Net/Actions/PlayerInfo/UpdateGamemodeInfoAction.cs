namespace Obsidian.Net.Actions.PlayerInfo;

public class UpdateGamemodeInfoAction : InfoAction
{
    public override PlayerInfoAction Type => PlayerInfoAction.UpdateGamemode;
    public Gamemode Gamemode { get; init; }

    public UpdateGamemodeInfoAction(Gamemode gamemode) => this.Gamemode = gamemode;

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await stream.WriteVarIntAsync(this.Gamemode);
    }

    public override void Write(MinecraftStream stream)
    {
        stream.WriteVarInt(Gamemode);
    }
}
