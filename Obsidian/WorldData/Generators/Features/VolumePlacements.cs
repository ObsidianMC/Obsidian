using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private PlacementModifier RandomlySelectedPlacement(JsonElement json)
    {
        var choices = json.GetProperty("placements").EnumerateArray().Select(this.Modifier).ToArray();

        return (context, random, position, output) =>
        {
            var choice = choices[random.NextInt(choices.Length)];
            choice(context, random, position, output);
        };
    }

    private static PlacementModifier CuboidPlacement(JsonElement json)
    {
        var xz = ValueProviders.Int(json.GetProperty("xz_size"));
        var ySize = ValueProviders.Int(json.GetProperty("y_size"));
        var edges = !json.TryGetProperty("include_edges", out var edgeJson) || edgeJson.GetBoolean();
        var interior = !json.TryGetProperty("include_interior", out var insideJson) || insideJson.GetBoolean();

        return (_, random, origin, output) =>
        {
            // Vertical size is sampled first; each size is an inclusive extent, not a block count.
            var height = ySize(random);
            var width = xz(random);
            var depth = xz(random);

            for (var x = 0; x <= width; x++)
            {
                for (var y = 0; y <= height; y++)
                {
                    for (var z = 0; z <= depth; z++)
                    {
                        var onX = x == 0 || x == width;
                        var onY = y == 0 || y == height;
                        var onZ = z == 0 || z == depth;
                        var faces = (onX ? 1 : 0) + (onY ? 1 : 0) + (onZ ? 1 : 0);

                        if (!edges && faces >= 2 || !interior && faces == 0)
                        {
                            continue;
                        }

                        output.Add(origin + new Vector(x, y, z));
                    }
                }
            }
        };
    }

    private BlockPredicate VolumeMatch(JsonElement json)
    {
        var low = json.GetProperty("min");
        var high = json.GetProperty("max");
        var min = new Vector(low[0].GetInt32(), low[1].GetInt32(), low[2].GetInt32());
        var max = new Vector(high[0].GetInt32(), high[1].GetInt32(), high[2].GetInt32());
        var match = this.Predicate(json.GetProperty("match"));

        return (level, origin) =>
        {
            for (var x = min.X; x <= max.X; x++)
            {
                for (var z = min.Z; z <= max.Z; z++)
                {
                    for (var y = min.Y; y <= max.Y; y++)
                    {
                        if (!match(level, origin + new Vector(x, y, z)))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        };
    }
}
