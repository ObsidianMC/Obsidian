using Obsidian.Serializer.Attributes;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net.Packets.Play
{
    public class PlayerBlockPlacement : Packet
    {
        [PacketOrder(0)]
        public Position Location { get; private set; }

        [PacketOrder(1)]
        public BlockFace Face { get; private set; } // enum with face

        [PacketOrder(2)]
        public int Hand { get; private set; } // hand it was placed from. 0 is main, 1 is off

        [PacketOrder(3)]
        public float CursorX { get; private set; }

        [PacketOrder(4)]
        public float CursorY { get; private set; }

        [PacketOrder(5)]
        public float CursorZ { get; private set; }

        public PlayerBlockPlacement() : base(0x29) { }

        public PlayerBlockPlacement(byte[] data) : base(0x29, data) { }

        public PlayerBlockPlacement(Position loc, BlockFace face, int hand, float cursorx, float cursory, float cursorz) : base(0x29)
        {
            Location = loc;
            Face = face;
            Hand = hand;
            CursorX = cursorx;
            CursorY = cursory;
            CursorZ = cursorz;
        }
    }
}