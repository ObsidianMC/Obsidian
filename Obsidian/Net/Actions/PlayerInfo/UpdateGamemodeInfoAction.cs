namespace Obsidian.Net.Actions.PlayerInfo;

public class UpdateGamemodeInfoAction(Gamemode gamemode) : InfoAction
{
    public override PlayerInfoAction Type => PlayerInfoAction.UpdateGamemode;
    public Gamemode Gamemode { get; init; } = gamemode;

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(Gamemode);
    }
}
