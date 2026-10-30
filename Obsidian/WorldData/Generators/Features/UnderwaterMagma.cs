using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private static Feature UnderwaterMagma(JsonElement json)
    {
        var searchRange = json.GetProperty("floor_search_range").GetInt32();
        var radius = new Vector(json.GetProperty("placement_radius_around_floor").GetInt32());
        var probability = json.GetProperty("placement_probability_per_valid_position").GetSingle();

        return (context, random, origin) =>
        {
            var level = context.Level;
            var column = Column.Scan(level, origin, searchRange, block => block.Is(BlockIds.Water),
                block => !block.Is(BlockIds.Water));
            if (column?.Floor is not int floor)
            {
                return false;
            }

            var center = origin.AtY(floor);
            var placed = false;

            foreach (var position in FeatureHelpers.BetweenClosed(center - radius, center + radius))
            {
                if (random.NextFloat() >= probability || !MagmaEnclosed(level, position))
                {
                    continue;
                }

                level.SetBlock(position, BlocksRegistry.MagmaBlock);
                placed = true;
            }

            return placed;
        };
    }

    private static bool MagmaEnclosed(IWorldGenLevel level, Vector position)
    {
        var state = level.GetBlock(position);
        if (state.IsAir || state.Is(BlockIds.Water)
            || !MagmaFaceOccludes(level.GetBlock(position + Vector.Down), BlockFace.Up))
        {
            return false;
        }

        return FeatureHelpers.Horizontal.All(face =>
            MagmaFaceOccludes(level.GetBlock(position.Offset(face)), face.Opposite()));
    }

    private static bool MagmaFaceOccludes(IBlock block, BlockFace face) =>
        // Full opaque cubes occlude every face. Partial blocks use the existing per-face occlusion masks,
        // which distinguish a slab's top from its sides and a stair's back from its open side.
        block.IsSolidRender() || BlockLight.LightShapesOcclude(block, BlocksRegistry.Air, face);
}
