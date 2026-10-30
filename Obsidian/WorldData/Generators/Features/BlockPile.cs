using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature BlockPile(JsonElement json)
    {
        var provider = this.Provider(json.GetProperty("state_provider"));

        return (context, random, origin) =>
        {
            var level = context.Level;

            if (origin.Y < level.MinY + 5)
            {
                return false;
            }

            var radiusX = 2 + random.NextInt(2);
            var radiusZ = 2 + random.NextInt(2);
            var min = origin - new Vector(radiusX, 0, radiusZ);
            var max = origin + new Vector(radiusX, 1, radiusZ);

            foreach (var position in FeatureHelpers.BetweenClosed(min, max))
            {
                var dx = origin.X - position.X;
                var dz = origin.Z - position.Z;
                var threshold = random.NextFloat() * 10.0f - random.NextFloat() * 6.0f;
                var inPile = dx * dx + dz * dz <= threshold;

                if (!inPile && (double)random.NextFloat() >= 0.031)
                {
                    continue;
                }

                if (!level.GetBlock(position).IsAir)
                {
                    continue;
                }

                var below = level.GetBlock(position + Vector.Down);
                var supported = below.Is(BlockIds.DirtPath)
                    ? random.NextBoolean()
                    : below.IsFaceSturdy(BlockFace.Up);

                if (supported)
                {
                    level.SetBlock(position, State(provider, level, random, position));
                }
            }

            return true;
        };
    }
}
