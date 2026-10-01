using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A coral block with 2-3 bent arms reaching in one main direction, like vanilla's CoralClawFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:coral_claw")]
public sealed class CoralClawFeature : CoralFeatureBase
{
    public override string Type => "minecraft:coral_claw";

    protected override bool PlaceShape(IWorldGenLevel level, IRandomSource random, Vector origin, IBlock coral)
    {
        if (!PlaceCoralBlock(level, random, origin, coral))
            return false;

        var main = FeatureHelpers.RandomHorizontal(random);
        var arms = random.NextInt(2) + 2;
        var directions = FeatureHelpers.ShuffledCopy([main, main.ClockWise(), main.CounterClockWise()], random);

        foreach (var direction in directions.Take(arms))
        {
            var reach = random.NextInt(2) + 1;
            var position = origin.Offset(direction);
            BlockFace step;
            int length;

            if (direction == main)
            {
                step = main;
                length = random.NextInt(3) + 2;
            }
            else
            {
                position += Vector.Up;
                step = random.NextInt(2) == 0 ? direction : BlockFace.Up;
                length = random.NextInt(3) + 3;
            }

            for (var i = 0; i < reach && PlaceCoralBlock(level, random, position, coral); i++)
                position = position.Offset(step);

            position = position.Offset(step.Opposite()) + Vector.Up;

            for (var i = 0; i < length; i++)
            {
                position = position.Offset(main);
                if (!PlaceCoralBlock(level, random, position, coral))
                    break;

                if (random.NextFloat() < 0.25f)
                    position += Vector.Up;
            }
        }

        return true;
    }
}
