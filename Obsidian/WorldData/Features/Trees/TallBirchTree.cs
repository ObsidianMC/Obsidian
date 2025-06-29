using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Trees;

public class TallBirchTree(GenHelper helper, IChunk chunk) : BaseTree(helper, chunk, Material.BirchLeaves, Material.BirchLog, 11)
{
}
