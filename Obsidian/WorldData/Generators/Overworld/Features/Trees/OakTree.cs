namespace Obsidian.WorldData.Generators.Overworld.Features.Trees;

public class OakTree : BaseTree
{
    public OakTree(GenHelper helper, Chunk chunk) : base(helper, chunk, Material.OakLeaves, Material.OakLog, 7)
    {
    }
}
