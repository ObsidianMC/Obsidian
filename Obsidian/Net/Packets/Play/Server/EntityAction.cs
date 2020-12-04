using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Server
{
    public partial class EntityAction : IPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        [Field(1), ActualType(typeof(int)), VarLength]
        public EAction Action { get; set; }

        [Field(2), VarLength]
        public int JumpBoost { get; set; }

        public int Id => 0x1C;

        public EntityAction() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.EntityId = await stream.ReadVarIntAsync();
            this.Action = (EAction)await stream.ReadVarIntAsync();
            this.JumpBoost = await stream.ReadVarIntAsync();
        }

        public Task HandleAsync(Obsidian.Server server, Player player)
        {
            switch (this.Action)
            {
                case EAction.StartSneaking:
                    player.Sneaking = true;
                    break;
                case EAction.StopSneaking:
                    player.Sneaking = false;
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
                default:
                    break;
            }

            return Task.CompletedTask;
        }
    }

    public enum EAction
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
