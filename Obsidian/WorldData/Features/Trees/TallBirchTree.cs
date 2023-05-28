using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Trees;

public class TallBirchTree : BaseTree
{
    public TallBirchTree(GenHelper helper, Chunk chunk) : base(helper, chunk, Material.BirchLeaves, Material.BirchLog, 11)
    {
    }
}
