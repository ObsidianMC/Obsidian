using Obsidian.API.Events;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;
public partial class UseItemPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public Hand Hand { get; private set; }


    [Field(1), VarLength]
    public int Sequence { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Hand = reader.ReadVarInt<Hand>();
        this.Sequence = reader.ReadVarInt();
    }

    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        await server.EventDispatcher.ExecuteEventAsync(new PlayerInteractEventArgs(player, server)
        {
            Item = this.Hand == Hand.MainHand ? player.GetHeldItem() : player.GetOffHandItem(),
            Hand = this.Hand,
        });
    }
}
