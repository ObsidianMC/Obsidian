namespace Obsidian.Util
{
    public class Transform
    {
        public double X { get; set; } = 0;
        public double Y { get; set; } = 0;
        public double Z { get; set; } = 0;

        public Angle Pitch { get; set; } = 0;
        public Angle Yaw { get; set; } = 0;

        public Transform()
        {
        }

        public Transform(double x, double y, double z)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
        }

        public Transform(double x, double y, double z, Angle pitch, Angle yaw)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
            this.Pitch = pitch;
            this.Yaw = yaw;
        }

        public Position Position
        {
            get => new Position(X, Y, Z);
            set
            {
                this.X = value.X;
                this.Y = value.Y;
                this.Z = value.Z;
            }
        }
    }
}