namespace Obsidian.Net.Actions.PlayerInfo;

public class UpdatePingInfoAction(int ping) : InfoAction
{
    public override PlayerInfoAction Type => PlayerInfoAction.UpdateLatency;
    public int Ping { get; set; } = ping;

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(Ping);
    }
}
