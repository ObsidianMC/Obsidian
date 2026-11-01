using Obsidian.API.World.Features;
using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature Delta(JsonElement json)
    {
        var contents = ReadBlock(json.GetProperty("contents"));
        var rim = ReadBlock(json.GetProperty("rim"));
        var size = this.Int(json.GetProperty("size"));
        var rimSize = this.Int(json.GetProperty("rim_size"));

        return (context, random, origin) =>
        {
            var level = context.Level;
            var useRim = random.NextDouble() < 0.9;
            var offsetX = useRim ? rimSize(random) : 0;
            var offsetZ = useRim ? rimSize(random) : 0;
            var hasRim = useRim && offsetX != 0 && offsetZ != 0;
            var sizeX = size(random);
            var sizeZ = size(random);
            var radius = Math.Max(sizeX, sizeZ);
            var placed = false;

            foreach (var position in FeatureHelpers.WithinManhattan(origin, sizeX, 0, sizeZ))
            {
                if (FeatureHelpers.DistManhattan(position, origin) > radius)
                {
                    break;
                }

                if (!DeltaClear(level, position, contents))
                {
                    continue;
                }

                if (hasRim)
                {
                    level.SetBlock(position, rim);
                    placed = true;
                }

                var shifted = position + new Vector(offsetX, 0, offsetZ);

                if (DeltaClear(level, shifted, contents))
                {
                    level.SetBlock(shifted, contents);
                    placed = true;
                }
            }

            return placed;
        };
    }

    private static bool DeltaClear(IWorldGenLevel level, Vector position, IBlock contents)
    {
        var state = level.GetBlock(position);
        var protectedBlock = state.RegistryId is BlockIds.Bedrock or BlockIds.NetherBricks
            or BlockIds.NetherBrickFence or BlockIds.NetherBrickStairs or BlockIds.NetherWart
            or BlockIds.Chest or BlockIds.Spawner;

        if (state.RegistryId == contents.RegistryId || protectedBlock)
        {
            return false;
        }

        return FeatureHelpers.Directions.All(face =>
            level.GetBlock(position.Offset(face)).IsAir == (face == BlockFace.Up));
    }
}
