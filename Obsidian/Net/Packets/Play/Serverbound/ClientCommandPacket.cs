using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ClientCommandPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public ClientAction Action { get; private set; }

    public async override ValueTask HandleAsync(Server server, Player player)
    {
        if (Action == ClientAction.PerformRespawn)
        {
            await player.RespawnAsync();
        }
    }

    public override void Populate(INetStreamReader reader) => this.Action = (ClientAction)reader.ReadVarInt();
}

public enum ClientAction
{
    PerformRespawn,
    RequestStats
}
