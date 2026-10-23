using Obsidian.WorldData.Generators;
using Obsidian.API.BlockStates.Builders;

namespace Obsidian.WorldData.Features.Flora;

public class RoseBushFlora(GenHelper helper, IChunk chunk) :
    BaseTallFlora(helper, chunk, BlocksRegistry.RoseBush, 2,
        new RoseBushStateBuilder().WithHalf(BlockHalf.Lower).Build(),
        new RoseBushStateBuilder().WithHalf(BlockHalf.Upper).Build())
{
}
