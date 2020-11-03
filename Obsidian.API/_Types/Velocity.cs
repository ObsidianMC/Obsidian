namespace Obsidian.API
{
    public struct Velocity
    {
        public short X;
        public short Y;
        public short Z;

        public Velocity(short x, short y, short z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}