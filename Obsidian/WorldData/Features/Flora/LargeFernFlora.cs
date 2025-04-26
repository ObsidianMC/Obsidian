using Obsidian.API.BlockStates.Builders;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class LargeFernFlora(GenHelper helper, IChunk chunk) : 
    BaseTallFlora(helper, chunk, Material.LargeFern, 2, 
        new LargeFernStateBuilder().WithHalf(BlockHalf.Lower).Build(), new LargeFernStateBuilder().WithHalf(BlockHalf.Upper).Build())
{
}
