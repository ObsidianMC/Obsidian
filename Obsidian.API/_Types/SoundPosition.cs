namespace Obsidian.API
{
    public struct SoundPosition
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }

        public SoundPosition(int x, int y, int z)
        {
            this.X = x * 8;
            this.Y = y * 8;
            this.Z = z * 8;
        }

        public SoundPosition(double x, double y, double z)
        {
            this.X = (int)(x * 8);
            this.Y = (int)(y * 8);
            this.Z = (int)(z * 8);
        }

        public bool Match(int x, int y, int z) => this.X == x && this.Y == y && this.Z == z;

        public override string ToString() => $"{X}:{Y}:{Z}";
    }
}
