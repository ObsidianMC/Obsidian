using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A huge red mushroom with a dome cap hanging down three blocks, like vanilla's HugeRedMushroomFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:huge_red_mushroom")]
public sealed class HugeRedMushroomFeature : HugeMushroomFeatureBase
{
    public override string Type => "minecraft:huge_red_mushroom";

    /// <remarks>
    /// With vanilla's <c>height</c> argument of -1 this is always 0, so only the stem column must be clear.
    /// </remarks>
    protected override int GetTreeRadiusForHeight(int unused, int height, int foliageRadius, int y)
    {
        if (y < height && y >= height - 3)
            return foliageRadius;

        return y == height ? foliageRadius : 0;
    }

    protected override void MakeCap(IWorldGenLevel level, IRandomSource random, Vector origin, int height)
    {
        for (var y = height - 3; y <= height; y++)
        {
            var radius = y < height ? this.FoliageRadius : this.FoliageRadius - 1;
            var inner = this.FoliageRadius - 2;

            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dz = -radius; dz <= radius; dz++)
                {
                    var edgeX = dx == -radius || dx == radius;
                    var edgeZ = dz == -radius || dz == radius;
                    if (y < height && edgeX == edgeZ)
                        continue;

                    var state = this.CapProvider.GetState(random, origin);
                    if (state.HasProperty("west") && state.HasProperty("east") && state.HasProperty("north") && state.HasProperty("south")
                        && state.HasProperty("up"))
                    {
                        state = state.WithProperty("up", y >= height - 1).WithProperty("west", dx < -inner).WithProperty("east", dx > inner)
                            .WithProperty("north", dz < -inner).WithProperty("south", dz > inner);
                    }

                    PlaceMushroomBlock(level, origin + new Vector(dx, y, dz), state);
                }
            }
        }
    }
}
