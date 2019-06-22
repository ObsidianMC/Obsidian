namespace Obsidian.Util
{
    public class Transform
    {
        public Transform()
        {
        }

        public Transform(double x, double y, double z, float pitch = 0, float yaw = 0)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
            this.Pitch = pitch;
            this.Yaw = yaw;
        }

        public double X { get; set; } = 0;
        public double Y { get; set; } = 0;
        public double Z { get; set; } = 0;

        public float Pitch { get; set; } = 0;
        public float Yaw { get; set; } = 0;

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