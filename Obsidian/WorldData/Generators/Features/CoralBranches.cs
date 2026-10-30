using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature CoralTree(JsonElement json)
    {
        var child = this.Placed(json.GetProperty("feature"));

        return (context, random, origin) =>
        {
            var height = random.NextInt(3) + 1;
            var top = origin;

            for (var i = 0; i < height; i++)
            {
                if (!child.Place(context, random, top))
                {
                    return true;
                }

                top += Vector.Up;
            }

            var branchCount = random.NextInt(3) + 2;
            var directions = FeatureHelpers.Horizontal.ToArray();
            FeatureHelpers.Shuffle(directions.AsSpan(), random);

            foreach (var direction in directions.Take(branchCount))
            {
                var position = top.Offset(direction);
                var length = random.NextInt(5) + 2;
                var straight = 0;

                for (var i = 0; i < length; i++)
                {
                    if (!child.Place(context, random, position))
                    {
                        break;
                    }

                    straight++;
                    position += Vector.Up;

                    if (i == 0 || straight >= 2 && random.NextFloat() < 0.25f)
                    {
                        position = position.Offset(direction);
                        straight = 0;
                    }
                }
            }

            return true;
        };
    }

    private Feature CoralClaw(JsonElement json)
    {
        var child = this.Placed(json.GetProperty("feature"));

        return (context, random, origin) =>
        {
            if (!child.Place(context, random, origin))
            {
                return false;
            }

            var forward = FeatureHelpers.RandomHorizontal(random);
            var branchCount = random.NextInt(2) + 2;
            BlockFace[] directions = [forward, forward.ClockWise(), forward.CounterClockWise()];
            FeatureHelpers.Shuffle(directions.AsSpan(), random);

            foreach (var side in directions.Take(branchCount))
            {
                var baseLength = random.NextInt(2) + 1;
                var position = origin.Offset(side);
                var initialDirection = forward;
                int length;

                if (side == forward)
                {
                    length = random.NextInt(3) + 2;
                }
                else
                {
                    position += Vector.Up;
                    initialDirection = random.NextInt(2) == 0 ? side : BlockFace.Up;
                    length = random.NextInt(3) + 3;
                }

                for (var i = 0; i < baseLength; i++)
                {
                    if (!child.Place(context, random, position))
                    {
                        break;
                    }

                    position = position.Offset(initialDirection);
                }

                position = position.Offset(initialDirection.Opposite()) + Vector.Up;

                for (var i = 0; i < length; i++)
                {
                    position = position.Offset(forward);

                    if (!child.Place(context, random, position))
                    {
                        break;
                    }

                    if (random.NextFloat() < 0.25f)
                    {
                        position += Vector.Up;
                    }
                }
            }

            return true;
        };
    }
}
