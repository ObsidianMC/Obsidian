using Obsidian.API;

namespace Obsidian.Blocks
{
    public class BlockWorkbench : Block
    {
        internal BlockWorkbench(string name, int id) : base(name, id, Materials.CraftingTable)
        {
        }
    }
}
