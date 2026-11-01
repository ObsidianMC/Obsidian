using Obsidian.WorldData.Generators.Density;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>Finds the initial chunk from the noise settings' 26.3 density-function targets.</summary>
internal static class SpawnSearch
{
    public static (int X, int Z) Find(
        ImmutableArray<ImmutableDictionary<string, SpawnTargetRange>> targets,
        DensityCompiler compiler)
    {
        if (targets.IsEmpty)
            return (0, 0);

        var compiled = targets.Select(target => target.Select(entry => (
            Function: compiler.Function(entry.Key),
            Min: Quantize(entry.Value.Min),
            Max: Quantize(entry.Value.Max))).ToArray()).ToArray();
        var best = Score(0, 0);

        // NoiseSpawnFinder uses float angles/radii, strict improvement, and a fixed origin for each sweep.
        foreach (var (limit, step) in new[] { (2048f, 512f), (512f, 32f) })
        {
            var origin = best;
            var angle = 0f;
            for (var radius = step; radius <= limit;)
            {
                var candidate = Score(
                    origin.X + (int)(Math.Sin(angle) * radius),
                    origin.Z + (int)(Math.Cos(angle) * radius));
                if (candidate.Fitness < best.Fitness)
                    best = candidate;

                angle += step / radius;
                if (angle > Math.PI * 2)
                {
                    angle = 0f;
                    radius += step;
                }
            }
        }

        return (best.X, best.Z);

        Candidate Score(int x, int z)
        {
            var closest = long.MaxValue;
            foreach (var target in compiled)
            {
                var fitness = 0L;
                foreach (var parameter in target)
                {
                    var value = Quantize(parameter.Function.Sample((x >> 2) << 2, 0, (z >> 2) << 2));
                    var distance = Math.Max(Math.Max(parameter.Min - value, value - parameter.Max), 0);
                    fitness += distance * distance;
                }

                closest = Math.Min(closest, fitness);
            }

            return new Candidate(x, z, unchecked(closest * 2048L * 2048L + (long)x * x + (long)z * z));
        }
    }

    private static long Quantize(float value) => (long)(value * 10000f);

    private readonly record struct Candidate(int X, int Z, long Fitness);
}
