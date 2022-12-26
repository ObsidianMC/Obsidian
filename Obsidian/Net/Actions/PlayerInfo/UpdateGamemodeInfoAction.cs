namespace Obsidian.Net.Actions.PlayerInfo;

public class UpdateGamemodeInfoAction : InfoAction
{
    public override PlayerInfoAction Type => PlayerInfoAction.UpdateGamemode;
    public int Gamemode { get; init; }

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteVarIntAsync(this.Gamemode);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteVarInt(Gamemode);
    }
}
