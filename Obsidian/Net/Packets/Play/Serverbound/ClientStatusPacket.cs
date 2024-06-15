using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ClientStatusPacket : IServerboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public ClientAction Action { get; private set; }

    public int Id => 0x09;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        if (Action == ClientAction.PerformRespawn)
        {
            await player.RespawnAsync();
        }
    }
}

public enum ClientAction
{
    PerformRespawn,
    RequestStats
}
