using Obsidian.Util;
using Obsidian.Util.DataTypes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class PlayerBlockPlacement : Packet
    {
        public Position Location { get; private set; }
        public BlockFace Face { get; private set; } // enum with face
        public int Hand { get; private set; } // hand it was placed from. 0 is main, 1 is off
        public float CursorX { get; private set; }
        public float CursorY { get; private set; }
        public float CursorZ { get; private set; }

        public PlayerBlockPlacement(Position loc, BlockFace face, int hand, float cursorx, float cursory, float cursorz) : base(0x29, System.Array.Empty<byte>())
        {
            Location = loc;
            Face = face;
            Hand = hand;
            CursorX = cursorx;
            CursorY = cursory;
            CursorZ = cursorz;
        }

        public PlayerBlockPlacement(byte[] data) : base(0x29, data)
        {
        }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WritePositionAsync(Location);
            await stream.WriteVarIntAsync(Face);
            await stream.WriteVarIntAsync(Hand);
            await stream.WriteFloatAsync(CursorX);
            await stream.WriteFloatAsync(CursorY);
            await stream.WriteFloatAsync(CursorZ);
        }

        protected override async Task PopulateAsync(MinecraftStream stream)
        {
            Location = await stream.ReadPositionAsync();
            Face = (BlockFace)await stream.ReadByteAsync();
            Hand = await stream.ReadVarIntAsync();
            CursorX = await stream.ReadFloatAsync();
            CursorY = await stream.ReadFloatAsync();
            CursorZ = await stream.ReadFloatAsync();
        }
    }
}