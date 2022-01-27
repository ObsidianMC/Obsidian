namespace Obsidian.Net.Actions.PlayerInfo;

public class UpdateGamemodeInfoAction : InfoAction
{
    public int Gamemode { get; set; }

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteVarIntAsync(Gamemode);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteVarInt(Gamemode);
    }
}
