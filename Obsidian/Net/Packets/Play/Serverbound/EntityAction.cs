using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class EntityAction : IServerboundPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; private set; }

        [Field(1), ActualType(typeof(int)), VarLength]
        public EAction Action { get; set; }

        [Field(2), VarLength]
        public int JumpBoost { get; set; }

        public int Id => 0x1C;

        public async ValueTask HandleAsync(Server server, Player player)
        {
            switch (Action)
            {
                case EAction.StartSneaking:
                    player.Sneaking = true;
                    break;
                case EAction.StopSneaking:
                    player.Sneaking = false;
                    player.Pose = Pose.Standing;
                    break;
                case EAction.LeaveBed:
                    player.Sleeping = false;
                    break;
                case EAction.StartSprinting:
                    player.Sprinting = true;
                    break;
                case EAction.StopSprinting:
                    player.Sprinting = false;
                    break;
                case EAction.StartJumpWithHorse:
                    break;
                case EAction.StopJumpWithHorse:
                    break;
                case EAction.OpenHorseInventory:
                    player.InHorseInventory = true;
                    break;
                case EAction.StartFlyingWithElytra:
                    player.FlyingWithElytra = true;
                    break;
            }

            await server.BroadcastPacketAsync(new EntityMetadata
            {
                EntityId = player.EntityId,
                Entity = player
            }, player.EntityId);
        }
    }

    public enum EAction : int
    {
        StartSneaking,
        StopSneaking,

        LeaveBed,

        StartSprinting,
        StopSprinting,

        StartJumpWithHorse,
        StopJumpWithHorse,
        OpenHorseInventory,

        StartFlyingWithElytra
    }
}
