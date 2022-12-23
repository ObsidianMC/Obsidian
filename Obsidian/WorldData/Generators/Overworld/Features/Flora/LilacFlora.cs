using Obsidian.API.BlockStates.Builders;

namespace Obsidian.WorldData.Generators.Overworld.Features.Flora;

public class LilacFlora : BaseTallFlora
{
    public LilacFlora(GenHelper helper, Chunk chunk) : 
        base(helper, chunk, Material.Lilac, 2, new LilacStateBuilder().WithHalf(EHalf.Lower).Build(), new LilacStateBuilder().WithHalf(EHalf.Upper).Build())
    {

    }
}
