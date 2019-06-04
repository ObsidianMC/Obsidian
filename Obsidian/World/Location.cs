namespace Obsidian.Entities
{
    public class Location
    {
        public Location() { }
        public Location(double x, double y, double z)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
        }

        public double X { get; set; } = 0;
        public double Y { get; set; } = 0;
        public double Z { get; set; } = 0;

        public float Pitch { get; set; } = 0;
        public float Yaw { get; set; } = 0;
    }
}
