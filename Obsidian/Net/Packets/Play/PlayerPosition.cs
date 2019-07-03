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

        public PlayerPosition(byte[] data) : base(0x10, data) { }

        [Variable(VariableType.Double)]
        public double X { get; set; }

        [Variable(VariableType.Double)]
        public double Y { get; set; }

        [Variable(VariableType.Double)]
        public double Z { get; set; }

        [Variable(VariableType.Boolean)]
        public bool OnGround { get; private set; } = false;


        public Position Position => new Position
        {
            X = this.X,

            Y = this.Y,

            Z = this.Z
        };
    }
}