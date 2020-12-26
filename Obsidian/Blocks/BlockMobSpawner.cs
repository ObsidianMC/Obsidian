using Obsidian.API;

namespace Obsidian.Blocks
{
    public class BlockMobSpawner : Block
    {
        internal BlockMobSpawner(string name, int id) : base(name, id, Materials.Spawner)
        {
        }
    }
}
