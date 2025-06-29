using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Trees;

public class SpruceTree(GenHelper helper, IChunk chunk) : BaseTree(helper, chunk, Material.SpruceLeaves, Material.SpruceLog, 9)
{
}
