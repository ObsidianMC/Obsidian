using Obsidian.API;

namespace Obsidian.Blocks
{
    public class BlockMycel : Block
    {
        internal BlockMycel(string name, int id) : base(name, id, Materials.Mycelium)
        {
        }
    }
}
