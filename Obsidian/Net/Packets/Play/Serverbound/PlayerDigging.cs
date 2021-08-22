using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class PlayerDigging : IServerboundPacket
    {
        [Field(0), ActualType(typeof(int)), VarLength]
        public DiggingStatus Status { get; private set; }

        [Field(1)]
        public Vector Position { get; private set; }

        [Field(2), ActualType(typeof(sbyte))]
        public BlockFace Face { get; private set; } // This is an enum of what face of the block is being hit

        public int Id => 0x1B;

        public ValueTask HandleAsync(Server server, Player player)
        {
            server.BroadcastPlayerDig(new PlayerDiggingStore
            {
                Player = player.Uuid,
                Packet = this
            });

            if (Status == DiggingStatus.FinishedDigging)
            {
                server.World.BlockUpdateNeighbors(Position);
            }
        }
    }

    public class PlayerDiggingStore
    {
        public Guid Player { get; init; }
        public PlayerDigging Packet { get; init; }
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
