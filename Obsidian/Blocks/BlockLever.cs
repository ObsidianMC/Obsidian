using Obsidian.API;

namespace Obsidian.Blocks
{
    public class BlockLever : Block
    {
        internal BlockLever(string name, int id) : base(name, id, Materials.Lever)
        {
        }
    }
}
