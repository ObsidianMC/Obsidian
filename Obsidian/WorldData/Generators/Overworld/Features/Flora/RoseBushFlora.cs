using Obsidian.API.BlockStates.Builders;

namespace Obsidian.WorldData.Generators.Overworld.Features.Flora;

public class RoseBushFlora : BaseTallFlora
{
    public RoseBushFlora(GenHelper helper, Chunk chunk) : 
        base(helper, chunk, Material.RoseBush, 2, new RoseBushStateBuilder().WithHalf(EHalf.Lower).Build(), new RoseBushStateBuilder().WithHalf(EHalf.Upper).Build())
    {

    }
}
