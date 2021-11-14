using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class UseItem : IServerboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public Hand Hand { get; private set; }

    public int Id => 0x2F;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        switch (Hand)
        {
            case Hand.MainHand:
                await server.Events.InvokePlayerInteractAsync(new PlayerInteractEventArgs(player)
                {
                    Item = player.GetHeldItem()
                });
                break;
            case Hand.OffHand:
                await server.Events.InvokePlayerInteractAsync(new PlayerInteractEventArgs(player)
                {
                    Item = player.GetOffHandItem()
                });
                break;
        }
    }
}

