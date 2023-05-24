using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Trees;

public class DarkOakTree : BaseTree
{
    public DarkOakTree(GenHelper helper, Chunk chunk) : base(helper, chunk, Material.DarkOakLeaves, Material.DarkOakLog, 8)
    {
    }
}
