using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A hollow, box-shaped coral cap without edges, sunk 1-3 blocks into the floor, like vanilla's CoralMushroomFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:coral_mushroom")]
public sealed class CoralMushroomFeature : CoralFeatureBase
{
    public override string Type => "minecraft:coral_mushroom";

    protected override bool PlaceShape(IWorldGenLevel level, IRandomSource random, Vector origin, IBlock coral)
    {
        var sizeY = random.NextInt(3) + 3;
        var sizeX = random.NextInt(3) + 3;
        var sizeZ = random.NextInt(3) + 3;
        var sink = random.NextInt(3) + 1;

        for (var x = 0; x <= sizeX; x++)
        {
            for (var y = 0; y <= sizeY; y++)
            {
                for (var z = 0; z <= sizeZ; z++)
                {
                    var edgeX = x == 0 || x == sizeX;
                    var edgeY = y == 0 || y == sizeY;
                    var edgeZ = z == 0 || z == sizeZ;

                    // Faces of the box only, never its edges; each face block is skipped with a 10% roll.
                    if (edgeX && edgeY || edgeZ && edgeY || edgeX && edgeZ || !(edgeX || edgeY || edgeZ))
                        continue;

                    if (random.NextFloat() < 0.1f)
                        continue;

                    PlaceCoralBlock(level, random, origin + new Vector(x, y - sink, z), coral);
                }
            }
        }

        return true;
    }
}
