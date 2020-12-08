using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public class PlayerDigging : IPacket
    {
        [Field(0, Type = DataType.VarInt)]
        public DiggingStatus Status { get; private set; }

        [Field(1)]
        public Position Location { get; private set; }

        [Field(2, Type = DataType.Byte)]
        public BlockFace Face { get; private set; } // This is an enum of what face of the block is being hit

        public int Id => 0x1B;

        public PlayerDigging() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.Status = (DiggingStatus)await stream.ReadVarIntAsync();
            this.Location = await stream.ReadPositionAsync();
            this.Face = (BlockFace)await stream.ReadByteAsync();
        }

        public async Task HandleAsync(Obsidian.Server server, Player player)
        {
            await server.BroadcastPlayerDigAsync(new PlayerDiggingStore
            {
                Player = player.Uuid,
                Packet = this
            });
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