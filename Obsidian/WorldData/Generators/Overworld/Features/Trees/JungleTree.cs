using Obsidian.API;

namespace Obsidian.WorldData.Generators.Overworld.Features.Trees;

public class JungleTree : BaseTree
{
    public JungleTree(World world) : base(world, Material.JungleLeaves, Material.JungleLog, 10)
    {
    }
}
