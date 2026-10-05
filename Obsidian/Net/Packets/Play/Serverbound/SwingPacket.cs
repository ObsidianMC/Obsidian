using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SwingPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public InteractionHand Hand { get; private set; }

    public override void Populate(INetStreamReader reader) => this.Hand = reader.ReadVarInt<InteractionHand>();

    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        var entities = player.GetEntitiesNear(player.ClientInformation.ViewDistance);
        foreach (var otherEntity in entities)
        {
            if (otherEntity is not Player otherPlayer)
                continue;

            switch (Hand)
            {
                case InteractionHand.MainHand:
                    await otherPlayer.Client.QueuePacketAsync(new AnimatePacket
                    {
                        EntityId = player.EntityId,
                        Animation = EntityAnimationType.SwingMainArm
                    });
                    break;

                case InteractionHand.OffHand:
                    await otherPlayer.Client.QueuePacketAsync(new AnimatePacket
                    {
                        EntityId = player.EntityId,
                        Animation = EntityAnimationType.SwingOffhand
                    });
                    break;
            }
        }
    }
}
