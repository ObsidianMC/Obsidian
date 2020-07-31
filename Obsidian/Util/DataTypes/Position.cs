namespace Obsidian.Util.DataTypes
{
    public class Position
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public bool Opt { get; set; }

        public Position() { }

        public Position(int x, int y, int z)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
        }

        public Position(double x, double y, double z)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
        }

        public bool Match(int x, int y, int z)
        {
            return this.X == x && this.Y == y && this.Z == z;
        }

        public override string ToString()
        {
            return $"{X}:{Y}:{Z}";
        }
    }

    public class SoundPosition
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }

        public SoundPosition() { }

        public SoundPosition(int x, int y, int z)
        {
            this.X = (int)((x * 8) / 32.0D);
            this.Y = (int)((y * 8) / 32.0D);
            this.Z = (int)((z * 8) / 32.0D);
        }

        public SoundPosition(double x, double y, double z)
        {
            this.X = (int)((x * 8) / 32.0D);
            this.Y = (int)((y * 8) / 32.0D);
            this.Z = (int)((z * 8) / 32.0D);
        }

        public bool Match(int x, int y, int z) => this.X == x && this.Y == y && this.Z == z;

        public override string ToString() => $"{X}:{Y}:{Z}";
    }
}
