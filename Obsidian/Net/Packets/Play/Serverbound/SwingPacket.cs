using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SwingPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public Hand Hand { get; private set; }

    public override void Populate(INetStreamReader reader) => this.Hand = reader.ReadVarInt<Hand>();

    public async override ValueTask HandleAsync(Server server, Player player)
    {
        var entities = player.GetEntitiesNear(player.ClientInformation.ViewDistance);
        foreach (var otherEntity in entities)
        {
            if (otherEntity is not Player otherPlayer)
                continue;

            switch (Hand)
            {
                case Hand.MainHand:
                    await otherPlayer.client.QueuePacketAsync(new AnimatePacket
                    {
                        EntityId = player.EntityId,
                        Animation = EntityAnimationType.SwingMainArm
                    });
                    break;

                case Hand.OffHand:
                    await otherPlayer.client.QueuePacketAsync(new AnimatePacket
                    {
                        EntityId = player.EntityId,
                        Animation = EntityAnimationType.SwingOffhand
                    });
                    break;
            }
        }
    }
}
