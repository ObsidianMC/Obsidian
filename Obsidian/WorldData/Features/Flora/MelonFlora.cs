using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class MelonFlora(GenHelper helper, IChunk chunk) : BaseFlora(helper, chunk, Material.Melon)
{
}
