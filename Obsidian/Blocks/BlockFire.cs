using Obsidian.API;

namespace Obsidian.Blocks
{
    public class BlockFire : Block
    {
        internal BlockFire(string name, int id) : base(name, id, Materials.Fire)
        {
        }
    }
}
