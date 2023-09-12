using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Trees;

public class BirchTree : BaseTree
{
    public BirchTree(GenHelper helper, Chunk chunk) : base(helper, chunk, Material.BirchLeaves, Material.BirchLog, 7)
    {
    }
}
