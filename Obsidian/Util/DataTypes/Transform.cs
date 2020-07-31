namespace Obsidian.Util.DataTypes
{
    //TODO merge this into position class???
    public class Transform
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

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

        public Position ToPosition
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