using Obsidian.API;
using Obsidian.Entities.AI;
using Xunit;

namespace Obsidian.Tests;

public sealed class MobTerrainSegment
{
    [Fact]
    public void SegmentIntersectsIgnoresBoxesBehindTheStart()
    {
        var behind = new BoundingBox(new VectorD(-2, -0.5, -0.5), new VectorD(-1, 0.5, 0.5));
        var ahead = new BoundingBox(new VectorD(0.5, -0.5, -0.5), new VectorD(1.5, 0.5, 0.5));
        var start = new VectorD(0, 0, 0);
        var direction = new VectorD(1, 0, 0);

        Assert.False(MobTerrain.SegmentIntersects(behind, start, direction));
        Assert.True(MobTerrain.SegmentIntersects(ahead, start, direction));
        Assert.Null(MobTerrain.RayIntersection(behind, start, direction));
    }
}
