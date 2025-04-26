using Obsidian.API.BlockStates.Builders;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Features.Flora;

public class LilacFlora(GenHelper helper, IChunk chunk) : 
    BaseTallFlora(helper, chunk, Material.Lilac, 2, new LilacStateBuilder().WithHalf(BlockHalf.Lower).Build(), new LilacStateBuilder().WithHalf(BlockHalf.Upper).Build())
{
}
