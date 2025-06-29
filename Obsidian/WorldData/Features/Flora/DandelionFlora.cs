using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class DandelionFlora(GenHelper helper, IChunk chunk) : BaseFlora(helper, chunk, Material.Dandelion)
{
}
