using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A short coral trunk with 2-4 climbing branches, like vanilla's CoralTreeFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:coral_tree")]
public sealed class CoralTreeFeature : CoralFeatureBase
{
    public override string Type => "minecraft:coral_tree";

    protected override bool PlaceShape(IWorldGenLevel level, IRandomSource random, Vector origin, IBlock coral)
    {
        var position = origin;
        var trunkHeight = random.NextInt(3) + 1;
        for (var i = 0; i < trunkHeight; i++)
        {
            if (!PlaceCoralBlock(level, random, position, coral))
                return true;

            position += Vector.Up;
        }

        var top = position;
        var branches = random.NextInt(3) + 2;
        var directions = FeatureHelpers.ShuffledCopy(FeatureHelpers.Horizontal, random);

        foreach (var direction in directions.Take(branches))
        {
            position = top.Offset(direction);
            var length = random.NextInt(5) + 2;
            var sinceTurn = 0;

            for (var i = 0; i < length && PlaceCoralBlock(level, random, position, coral); i++)
            {
                sinceTurn++;
                position += Vector.Up;

                if (i == 0 || sinceTurn >= 2 && random.NextFloat() < 0.25f)
                {
                    position = position.Offset(direction);
                    sinceTurn = 0;
                }
            }
        }

        return true;
    }
}
