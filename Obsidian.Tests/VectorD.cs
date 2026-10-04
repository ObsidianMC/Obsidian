using Obsidian.API;
using Obsidian.Entities;
using Xunit;

namespace Obsidian.Tests;

public class VectorDTests
{
    [Theory(DisplayName = "A position's block rounds each coordinate down, like vanilla's BlockPos.containing")]
    [InlineData(-0.5, 64.9, 3.2, -1, 64, 3)]
    [InlineData(-16.0, -0.01, 15.99, -16, -1, 15)]
    public void BlockRoundsDown(double x, double y, double z, int blockX, int blockY, int blockZ) =>
        Assert.Equal(new Vector(blockX, blockY, blockZ), (Vector)new VectorD(x, y, z));

    [Fact(DisplayName = "Positions are equal only when they're exactly the same")]
    public void ComparesExactly()
    {
        var position = new VectorD(0, 64, 0);
        var nearby = new VectorD(0.005, 64, 0);

        Assert.NotEqual(position, nearby);
        Assert.True(position.IsNear(nearby));
    }

    [Fact(DisplayName = "Relative moves round each position like vanilla, so moving back and forth doesn't drift")]
    public void MovesDontDrift()
    {
        VectorD start = new(0.5, 0, 0), end = new(0.4799, 0, 0);

        Assert.Equal(-82, Entity.MoveDelta(start, end).X);
        Assert.Equal(82, Entity.MoveDelta(end, start).X);
    }
}
