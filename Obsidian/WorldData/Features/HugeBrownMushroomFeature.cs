using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A huge brown mushroom with a flat square cap, like vanilla's HugeBrownMushroomFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:huge_brown_mushroom")]
public sealed class HugeBrownMushroomFeature : HugeMushroomFeatureBase
{
    public override string Type => "minecraft:huge_brown_mushroom";

    protected override int GetTreeRadiusForHeight(int unused, int height, int foliageRadius, int y) => y <= 3 ? 0 : foliageRadius;

    protected override void MakeCap(IWorldGenLevel level, IRandomSource random, Vector origin, int height)
    {
        var radius = this.FoliageRadius;
        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dz = -radius; dz <= radius; dz++)
            {
                var minX = dx == -radius;
                var maxX = dx == radius;
                var minZ = dz == -radius;
                var maxZ = dz == radius;
                var edgeX = minX || maxX;
                var edgeZ = minZ || maxZ;
                if (edgeX && edgeZ)
                    continue;

                var west = minX || edgeZ && dx == 1 - radius;
                var east = maxX || edgeZ && dx == radius - 1;
                var north = minZ || edgeX && dz == 1 - radius;
                var south = maxZ || edgeX && dz == radius - 1;

                var state = this.CapProvider.GetState(random, origin);
                if (state.HasProperty("west") && state.HasProperty("east") && state.HasProperty("north") && state.HasProperty("south"))
                {
                    state = state.WithProperty("west", west).WithProperty("east", east).WithProperty("north", north)
                        .WithProperty("south", south);
                }

                PlaceMushroomBlock(level, origin + new Vector(dx, height, dz), state);
            }
        }
    }
}
