using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.API;
using System;

namespace Obsidian.Net.Packets.Play.Server
{
    public class PlayerDigging : Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public DiggingStatus Status { get; private set; }

        [Field(1)]
        public Position Location { get; private set; }

        [Field(2, Type = DataType.Byte)]
        public BlockFace Face { get; private set; } // This is an enum of what face of the block is being hit

        public PlayerDigging() : base(0x1B) { }

        public PlayerDigging(byte[] packetdata) : base(0x1B, packetdata) { }
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