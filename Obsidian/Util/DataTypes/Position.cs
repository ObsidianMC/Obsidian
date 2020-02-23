namespace Obsidian.Util
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
            return $"X{X} Y{Y} Z{Z}";
        }
    }
}
