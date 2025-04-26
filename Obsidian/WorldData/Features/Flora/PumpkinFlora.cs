using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class PumpkinFlora(GenHelper helper, IChunk chunk) : BaseFlora(helper, chunk, Material.Pumpkin)
{
}
