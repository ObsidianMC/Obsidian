namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private static Feature EndIsland() => (context, random, origin) =>
    {
        var radius = random.NextInt(3) + 4.0f;
        var y = 0;

        while (radius > 0.5f)
        {
            var low = (int)MathF.Floor(-radius);
            var high = (int)MathF.Ceiling(radius);
            var limit = (radius + 1.0f) * (radius + 1.0f);

            for (var x = low; x <= high; x++)
            {
                for (var z = low; z <= high; z++)
                {
                    if (x * x + z * z <= limit)
                    {
                        context.Level.SetBlock(origin + new Vector(x, y, z), BlocksRegistry.EndStone);
                    }
                }
            }

            radius -= random.NextInt(2) + 0.5f;
            y--;
        }

        return true;
    };
}
