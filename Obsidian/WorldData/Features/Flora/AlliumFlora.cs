using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class AlliumFlora(GenHelper helper, IChunk chunk) : BaseFlora(helper, chunk, Material.Allium)
{
}
