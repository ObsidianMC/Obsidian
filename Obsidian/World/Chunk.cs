namespace Obsidian.World
{
    public class Chunk
    {
        public int X { get; }
        public int Z { get; }

        public Chunk(int x, int z)
        {
            this.X = x;
            this.Z = z;
        }
    }
}
