using Obsidian.API;

namespace Obsidian.Blocks
{
    public class BlockFurnace : Block
    {
        internal BlockFurnace(string name, int id) : base(name, id, Materials.Furnace)
        {
        }
    }
}
