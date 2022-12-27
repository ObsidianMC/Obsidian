namespace Obsidian.Net.Actions.PlayerInfo;

public class UpdatePingInfoAction : InfoAction
{
    public override PlayerInfoAction Type => PlayerInfoAction.UpdateLatency;
    public int Ping { get; set; }

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await stream.WriteVarIntAsync(this.Ping);
    }

    public override void Write(MinecraftStream stream)
    {
        stream.WriteVarInt(Ping);
    }
}
