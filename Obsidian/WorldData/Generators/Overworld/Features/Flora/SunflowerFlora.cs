using Obsidian.API.BlockStates.Builders;

namespace Obsidian.WorldData.Generators.Overworld.Features.Flora;

public class SunflowerFlora : BaseTallFlora
{
    public SunflowerFlora(GenHelper helper, Chunk chunk) : 
        base(helper, chunk, Material.Sunflower, 2, new SunflowerStateBuilder().WithHalf(EHalf.Lower).Build(), new SunflowerStateBuilder().WithHalf(EHalf.Upper).Build())
    {

    }
}
