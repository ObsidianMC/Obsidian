using Obsidian.API.BlockStates.Builders;

namespace Obsidian.WorldData.Generators.Overworld.Features.Flora;

public class PeonyFlora : BaseTallFlora
{
    public PeonyFlora(GenHelper helper, Chunk chunk) : 
        base(helper, chunk, Material.Peony, 2, new PeonyStateBuilder().WithHalf(EHalf.Lower).Build(), new PeonyStateBuilder().WithHalf(EHalf.Upper).Build())
    {

    }
}
