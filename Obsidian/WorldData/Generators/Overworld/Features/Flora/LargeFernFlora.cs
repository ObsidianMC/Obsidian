using Obsidian.API.BlockStates.Builders;

namespace Obsidian.WorldData.Generators.Overworld.Features.Flora;

public class LargeFernFlora : BaseTallFlora
{
    public LargeFernFlora(GenHelper helper, Chunk chunk) : 
        base(helper, chunk, Material.LargeFern, 2, new LargeFernStateBuilder().WithHalf(EHalf.Lower).Build(), new LargeFernStateBuilder().WithHalf(EHalf.Upper).Build())
    {

    }
}
