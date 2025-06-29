using Obsidian.API.BlockStates.Builders;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class PeonyFlora(GenHelper helper, IChunk chunk) : 
    BaseTallFlora(helper, chunk, Material.Peony, 2, new PeonyStateBuilder().WithHalf(BlockHalf.Lower).Build(), new PeonyStateBuilder().WithHalf(BlockHalf.Upper).Build())
{
}
