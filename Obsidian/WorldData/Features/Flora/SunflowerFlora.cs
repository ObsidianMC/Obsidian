using Obsidian.API.BlockStates.Builders;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class SunflowerFlora(GenHelper helper, IChunk chunk) : 
    BaseTallFlora(helper, chunk, Material.Sunflower, 2, new SunflowerStateBuilder().WithHalf(BlockHalf.Lower).Build(), 
        new SunflowerStateBuilder().WithHalf(BlockHalf.Upper).Build())
{
}
