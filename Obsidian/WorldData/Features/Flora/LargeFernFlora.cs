using Obsidian.API.BlockStates.Builders;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class LargeFernFlora : BaseTallFlora
{
    public LargeFernFlora(GenHelper helper, Chunk chunk) : 
        base(helper, chunk, Material.LargeFern, 2, new LargeFernStateBuilder().WithHalf(BlockHalf.Lower).Build(), new LargeFernStateBuilder().WithHalf(BlockHalf.Upper).Build())
    {

    }
}
