namespace Obsidian.Net.Actions.PlayerInfo;

public class UpdateGamemodeInfoAction(GameMode gamemode) : InfoAction
{
    public override PlayerInfoAction Type => PlayerInfoAction.UpdateGamemode;
    public GameMode GameMode { get; init; } = gamemode;

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(GameMode);
    }
}
