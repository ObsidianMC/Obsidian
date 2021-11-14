using Obsidian.API;

namespace Obsidian.WorldData.Generators.Overworld.Features.Trees;

public class SpruceTree : BaseTree
{
    public SpruceTree(World world) : base(world, Material.SpruceLeaves, Material.SpruceLog, 9)
    {
    }
}
