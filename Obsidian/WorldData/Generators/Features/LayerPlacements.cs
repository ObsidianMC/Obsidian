using Obsidian.API.World.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private static PlacementModifier FixedPlacement(JsonElement json)
    {
        var positions = json.GetProperty("positions").EnumerateArray()
            .Select(value => new Vector(value[0].GetInt32(), value[1].GetInt32(), value[2].GetInt32()))
            .ToArray();

        return (_, _, origin, output) =>
        {
            foreach (var position in positions)
            {
                if (position.X >> 4 == origin.X >> 4 && position.Z >> 4 == origin.Z >> 4)
                {
                    output.Add(position);
                }
            }
        };
    }

    private PlacementModifier CountOnEveryLayer(JsonElement json)
    {
        var count = this.Int(json.GetProperty("count"));

        return (context, random, origin, output) =>
        {
            var found = true;

            for (var layer = 0; found; layer++)
            {
                found = false;

                // 26.3 samples the count on every loop condition, including the final failed comparison.
                for (var i = 0; i < count(random); i++)
                {
                    var x = origin.X + random.NextInt(16);
                    var z = origin.Z + random.NextInt(16);
                    var top = context.GetHeight(HeightmapType.MotionBlocking, x, z);
                    var y = LayerGround(context.Decoration.Level, x, top, z, layer);

                    if (y is not null)
                    {
                        output.Add(new Vector(x, y.Value, z));
                        found = true;
                    }
                }
            }
        };
    }

    private static int? LayerGround(IWorldGenLevel level, int x, int top, int z, int layer)
    {
        var above = level.GetBlock(new Vector(x, top, z));
        var encountered = 0;

        for (var y = top; y >= level.MinY + 1; y--)
        {
            var below = level.GetBlock(new Vector(x, y - 1, z));

            if (!LayerEmpty(below) && LayerEmpty(above) && !below.Is(BlockIds.Bedrock))
            {
                if (encountered == layer)
                {
                    return y;
                }

                encountered++;
            }

            above = below;
        }

        return null;
    }

    private static bool LayerEmpty(IBlock state) => state.IsAir || state.Is(BlockIds.Water) || state.Is(BlockIds.Lava);
}
