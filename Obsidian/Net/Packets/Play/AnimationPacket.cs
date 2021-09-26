using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public partial class AnimationPacket : IServerboundPacket
    {
        [Field(0), ActualType(typeof(int)), VarLength]
        public Hand Hand { get; private set; }

        public int Id => 0x2C;

        public async ValueTask HandleAsync(Server server, Player player)
        {
            // TODO broadcast entity animation to nearby players
            var entities = player.GetEntitiesNear(player.client.ClientSettings.ViewDistance);
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
}
