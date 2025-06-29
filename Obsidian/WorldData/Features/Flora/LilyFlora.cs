using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class LilyFlora(GenHelper helper, IChunk chunk) : BaseFlora(helper, chunk, Material.LilyOfTheValley)
{
}
