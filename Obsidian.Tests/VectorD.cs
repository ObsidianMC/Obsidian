using Obsidian.API;
using Obsidian.Entities;
using Xunit;

namespace Obsidian.Tests;

public class VectorDTests
{
    [Fact]
    public void RelativeMovesPreservePrecisionAndTeleportLongDistances()
    {
        Assert.Equal(1, Entity.MoveDelta(VectorD.Zero, new VectorD(0.5 / 4096, 0, 0)).X);
        Assert.Equal(-1, Entity.MoveDelta(VectorD.Zero, new VectorD(-0.5000000000000001 / 4096, 0, 0)).X);

        {
            VectorD start = new(0.5, 0, 0);
            VectorD end = new(0.4799, 0, 0);

            Assert.Equal(-82, Entity.MoveDelta(start, end).X);
            Assert.Equal(82, Entity.MoveDelta(end, start).X);
        }

        {
            // Eight blocks along one axis is 32768 in 1/4096 blocks, one more than a short holds.
            var delta = Entity.MoveDelta(new VectorD(0.0001, 0, 0), new VectorD(8, 0, 0));

            Assert.Equal(32768, delta.X);
            Assert.False(Entity.IsRelativeMove(delta));
            Assert.True(Entity.IsRelativeMove(Entity.MoveDelta(VectorD.Zero, new VectorD(7.9, -7.9, 0))));

            // 1,048,576 blocks is 2^32 units, which would wrap to 0 as an int.
            Assert.False(Entity.IsRelativeMove(Entity.MoveDelta(VectorD.Zero, new VectorD(1_048_576, 0, 0))));
        }
    }

}
