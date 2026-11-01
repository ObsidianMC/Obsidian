using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature ReplaceBlobs(JsonElement json)
    {
        var target = ReadBlock(json.GetProperty("target"));
        var replacement = ReadBlock(json.GetProperty("state"));
        var radius = this.Int(json.GetProperty("radius"));

        return (context, random, origin) =>
        {
            var level = context.Level;
            var top = level.MinY + level.Height - 1;
            var y = Math.Clamp(origin.Y, level.MinY + 1, top);

            while (y > level.MinY + 1 && level.GetBlock(origin.AtY(y)).RegistryId != target.RegistryId)
            {
                y--;
            }

            if (y <= level.MinY + 1)
            {
                return false;
            }

            var center = origin.AtY(y);
            var radiusX = radius(random);
            var radiusY = radius(random);
            var radiusZ = radius(random);
            var limit = Math.Max(radiusX, Math.Max(radiusY, radiusZ));
            var placed = false;

            // Writes do not draw randoms or inspect neighbors, so the diamond can be visited in our volume order.
            for (var dx = -radiusX; dx <= radiusX; dx++)
            {
                for (var dy = -radiusY; dy <= radiusY; dy++)
                {
                    for (var dz = -radiusZ; dz <= radiusZ; dz++)
                    {
                        if (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) > limit)
                        {
                            continue;
                        }

                        var position = center + new Vector(dx, dy, dz);

                        if (level.GetBlock(position).RegistryId == target.RegistryId)
                        {
                            level.SetBlock(position, replacement);
                            placed = true;
                        }
                    }
                }
            }

            return placed;
        };
    }
}
