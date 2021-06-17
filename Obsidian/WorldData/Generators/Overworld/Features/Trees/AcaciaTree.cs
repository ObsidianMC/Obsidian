using Obsidian.API;

namespace Obsidian.WorldData.Generators.Overworld.Features.Trees
{
    public class AcaciaTree : BaseTree
    {
        public AcaciaTree(World world) : base(world, Material.AcaciaLeaves, Material.AcaciaLog, 6)
        {
        }
    }
}
