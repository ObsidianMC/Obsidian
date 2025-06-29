using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Trees;

public class OakTree(GenHelper helper, IChunk chunk) : BaseTree(helper, chunk, Material.OakLeaves, Material.OakLog, 7)
{
}
