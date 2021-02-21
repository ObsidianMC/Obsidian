using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class PlayerDigging : IPacket
    {
        [Field(0), ActualType(typeof(int)), VarLength]
        public DiggingStatus Status { get; private set; }

        [Field(1)]
        public Position Position { get; private set; }

        [Field(2), ActualType(typeof(sbyte))]
        public BlockFace Face { get; private set; } // This is an enum of what face of the block is being hit

        public int Id => 0x1B;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Status = (DiggingStatus)await stream.ReadVarIntAsync();
            this.Position = await stream.ReadPositionAsync();
            this.Face = (BlockFace)await stream.ReadByteAsync();
        }

        public async Task HandleAsync(Server server, Player player)
        {
            server.BroadcastPlayerDig(new PlayerDiggingStore
            {
                Player = player.Uuid,
                Packet = this
            });
            await Task.CompletedTask;
        }
    }

    public class PlayerDiggingStore
    {
        public Guid Player { get; set; }
        public PlayerDigging Packet { get; set; }
    }

    public enum DiggingStatus : int
    {
        StartedDigging,
        CancelledDigging,
        FinishedDigging,

        DropItemStack,
        DropItem,

        ShootArrowOrFinishEating,

        SwapItemInHand
    }
}