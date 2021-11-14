using System.Threading.Tasks;

namespace Obsidian.Net.Actions.PlayerInfo;

public class UpdatePingInfoAction : InfoAction
{
    public int Ping { get; set; }

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteVarIntAsync(this.Ping);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteVarInt(Ping);
    }
}
