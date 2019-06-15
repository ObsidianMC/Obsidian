using fNbt.Serialization;
using Obsidian.Entities;

namespace Obsidian.World
{
    public abstract class TileEntity
    {
        [NbtIgnore]
        public Location Location
        {
            get
            {
                return new Location(X, Y, Z);
            }
        }

        [TagName("id")]
        public abstract string Id { get; }
        [TagName("x")]
        public int X { get; set; }
        [TagName("y")]
        public int Y { get; set; }
        [TagName("z")]
        public int Z { get; set; }
    }
}
