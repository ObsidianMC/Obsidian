using Obsidian.Util;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net.Packets
{
    public class PlayerBlockPlacement : Packet
    {
        [Variable]
        public Position Location { get; private set; }

        [Variable]
        public BlockFace Face { get; private set; } // enum with face

        [Variable]
        public int Hand { get; private set; } // hand it was placed from. 0 is main, 1 is off

        [Variable]
        public float CursorX { get; private set; }

        [Variable]
        public float CursorY { get; private set; }

        [Variable]
        public float CursorZ { get; private set; }

        public PlayerBlockPlacement(Position loc, BlockFace face, int hand, float cursorx, float cursory, float cursorz) : base(0x29, new byte[0])
        {
            Location = loc;
            Face = face;
            Hand = hand;
            CursorX = cursorx;
            CursorY = cursory;
            CursorZ = cursorz;
        }

        public PlayerBlockPlacement(byte[] data) : base(0x29, data) { }
    }
}