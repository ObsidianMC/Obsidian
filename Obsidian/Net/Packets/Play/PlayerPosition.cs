using Obsidian.Util;

namespace Obsidian.Net.Packets
{
    public class PlayerPosition : Packet
    {
        public PlayerPosition(Position pos, bool onground) : base(0x10, new byte[0])
        {
            this.X = pos.X;
            this.Y = pos.Y;
            this.Z = pos.Z;
            this.OnGround = onground;
        }

        public PlayerPosition(byte[] data) : base(0x10, data)
        {
        }

        [Variable]
        public double X { get; set; }

        [Variable]
        public double Y { get; set; }

        [Variable]
        public double Z { get; set; }

        [Variable]
        public bool OnGround { get; private set; } = false;

        public Position Position => new Position
        {
            X = this.X,

            Y = this.Y,

            Z = this.Z
        };
    }
}