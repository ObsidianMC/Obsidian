using Obsidian.API.BlockStates.Builders;

namespace Obsidian.WorldData.Generators.Overworld.Features.Flora;

public class LilacFlora : BaseTallFlora
{
    public LilacFlora(GenHelper helper, Chunk chunk) : 
        base(helper, chunk, Material.Lilac, 2, new LilacStateBuilder().WithHalf(BlockHalf.Lower).Build(), new LilacStateBuilder().WithHalf(BlockHalf.Upper).Build())
    {

    }
}
