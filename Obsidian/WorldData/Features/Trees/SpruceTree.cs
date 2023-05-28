using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Trees;

public class SpruceTree : BaseTree
{
    public SpruceTree(GenHelper helper, Chunk chunk) : base(helper, chunk, Material.SpruceLeaves, Material.SpruceLog, 9)
    {
    }
}
