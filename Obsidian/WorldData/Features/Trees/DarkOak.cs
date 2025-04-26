using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Trees;

public class DarkOakTree(GenHelper helper, IChunk chunk) : BaseTree(helper, chunk, Material.DarkOakLeaves, Material.DarkOakLog, 8)
{
}
