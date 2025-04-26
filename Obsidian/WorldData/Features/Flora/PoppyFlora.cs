using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class PoppyFlora(GenHelper helper, IChunk chunk) : BaseFlora(helper, chunk, Material.Poppy)
{
}
