using Obsidian.API;

namespace Obsidian.WorldData
{
    public abstract class TileEntity
    {
        //[NbtIgnore]
        public PositionF Position
        {
            get
            {
                return new PositionF(X, Y, Z);
            }
        }

        //[TagName("id")]
        public abstract string Id { get; }
        //[TagName("x")]
        public int X { get; set; }
        //[TagName("y")]
        public int Y { get; set; }
        //[TagName("z")]
        public int Z { get; set; }
    }
}
