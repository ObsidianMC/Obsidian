using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Generators.Features;

/// <summary>Independent ore attempts around the origin, with 26.3's draw order and Java rounding.</summary>
internal sealed class ScatteredOre((RuleTest Target, IBlock State)[] targets, int size, float discardChance)
{
    public bool Place(DecorationContext context, IRandomSource random, Vector origin)
    {
        var level = context.Level;
        var attempts = random.NextInt(size + 1);

        for (var i = 0; i < attempts; i++)
        {
            var spread = Math.Min(i, 7);
            var dx = Offset(random, spread);
            var dy = Offset(random, spread);
            var dz = Offset(random, spread);
            var position = origin + new Vector(dx, dy, dz);
            var existing = level.GetBlock(position);

            foreach (var (target, state) in targets)
            {
                if (!target(existing, position, random))
                {
                    continue;
                }

                var skipAirCheck = discardChance <= 0.0f
                    || discardChance < 1.0f && random.NextFloat() >= discardChance;
                var exposed = !skipAirCheck && IsAdjacentToAir(level, position);

                if (exposed)
                {
                    continue;
                }

                level.SetBlock(position, state);
                break;
            }
        }

        // Vanilla reports completion even when no target matched or the attempt count was zero.
        return true;
    }

    private static bool IsAdjacentToAir(IWorldGenLevel level, Vector position)
    {
        foreach (var face in FeatureHelpers.Directions)
        {
            if (level.GetBlock(position.Offset(face)).IsAir)
                return true;
        }

        return false;
    }

    private static int Offset(IRandomSource random, int spread)
    {
        var value = (random.NextFloat() - random.NextFloat()) * spread;
        return (int)MathF.Floor(value + 0.5f);
    }
}
