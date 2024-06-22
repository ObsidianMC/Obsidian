using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SwingArmPacket : IServerboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public Hand Hand { get; private set; }

    public int Id => 0x36;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        var entities = player.GetEntitiesNear(player.ClientInformation.ViewDistance);
        foreach (var otherEntity in entities)
        {
            if (otherEntity is not Player otherPlayer)
                continue;

            switch (Hand)
            {
                case Hand.MainHand:
                    await otherPlayer.client.QueuePacketAsync(new EntityAnimationPacket
                    {
                        EntityId = player.EntityId,
                        Animation = EntityAnimationType.SwingMainArm
                    });
                    break;

                case Hand.OffHand:
                    await otherPlayer.client.QueuePacketAsync(new EntityAnimationPacket
                    {
                        EntityId = player.EntityId,
                        Animation = EntityAnimationType.SwingOffhand
                    });
                    break;
            }
        }
    }
}
