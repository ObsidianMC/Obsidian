using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class CornflowerFlora(GenHelper helper, IChunk chunk) : BaseFlora(helper, chunk, Material.Cornflower)
{
}
