using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public partial class Animation : IClientboundPacket, IServerboundPacket
    {
        [Field(0), ActualType(typeof(int)), VarLength]
        public Hand Hand { get; private set; }

        public int Id => 0x2C;

        public async ValueTask HandleAsync(Server server, Player player)
        {
            // TODO broadcast entity animation to nearby players
            switch (Hand)
            {
                case Hand.Right:
                    await server.BroadcastPacketAsync(new EntityAnimation
                    {
                        EntityId = player.EntityId,
                        Animation = EAnimation.SwingMainArm
                    }, player);
                    break;

                case Hand.OffHand:
                    await server.BroadcastPacketAsync(new EntityAnimation
                    {
                        EntityId = player.EntityId,
                        Animation = EAnimation.SwingOffhand
                    }, player);
                    break;
            }
        }
    }
}
