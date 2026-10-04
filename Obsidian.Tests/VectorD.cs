using Obsidian.API;
using Xunit;

namespace Obsidian.Tests;

public class VectorDTests
{
    [Theory(DisplayName = "A position's block rounds each coordinate down, like vanilla's BlockPos.containing")]
    [InlineData(-0.5, 64.9, 3.2, -1, 64, 3)]
    [InlineData(-16.0, -0.01, 15.99, -16, -1, 15)]
    public void BlockRoundsDown(double x, double y, double z, int blockX, int blockY, int blockZ) =>
        Assert.Equal(new Vector(blockX, blockY, blockZ), (Vector)new VectorD(x, y, z));
}
