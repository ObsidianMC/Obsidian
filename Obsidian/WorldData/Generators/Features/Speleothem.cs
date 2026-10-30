using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature Speleothem(JsonElement json)
    {
        var blocks = this.SpeleothemBlocks(json);
        var taller = FloatOr(json, "chance_of_taller_generation", 0.2f);
        var spread = FloatOr(json, "chance_of_directional_spread", 0.7f);
        var radius2 = FloatOr(json, "chance_of_spread_radius2", 0.5f);
        var radius3 = FloatOr(json, "chance_of_spread_radius3", 0.5f);

        return (context, random, origin) =>
        {
            var level = context.Level;
            var above = blocks.IsBase(level.GetBlock(origin + Vector.Up));
            var below = blocks.IsBase(level.GetBlock(origin + Vector.Down));

            if (!above && !below)
            {
                return false;
            }

            var downward = above && (!below || random.NextBoolean());
            var direction = downward ? BlockFace.Down : BlockFace.Up;
            var basePosition = origin.Offset(direction.Opposite());
            blocks.PlaceBase(level, basePosition);

            foreach (var face in FeatureHelpers.Horizontal)
            {
                if (random.NextFloat() > spread)
                {
                    continue;
                }

                var position = basePosition.Offset(face);
                blocks.PlaceBase(level, position);

                if (random.NextFloat() > radius2)
                {
                    continue;
                }

                position = position.Offset(FeatureHelpers.RandomDirection(random));
                blocks.PlaceBase(level, position);

                if (random.NextFloat() <= radius3)
                {
                    position = position.Offset(FeatureHelpers.RandomDirection(random));
                    blocks.PlaceBase(level, position);
                }
            }

            var tall = random.NextFloat() < taller && SpeleothemEmpty(level.GetBlock(origin.Offset(direction)));
            blocks.Grow(level, origin, direction, tall ? 2 : 1, merge: false);
            return true;
        };
    }

    private SpeleothemStates SpeleothemBlocks(JsonElement json) => new(
        ReadBlock(json.GetProperty("base_block")),
        ReadBlock(json.GetProperty("pointed_block")),
        this.Blocks(json.GetProperty("replaceable_blocks")));

    private static float FloatOr(JsonElement json, string name, float fallback) =>
        json.TryGetProperty(name, out var value) ? value.GetSingle() : fallback;

    private static bool SpeleothemEmpty(IBlock state) => state.IsAir || state.Is(BlockIds.Water);

    private sealed record SpeleothemStates(IBlock Base, IBlock Pointed, HashSet<int> Replaceable)
    {
        public bool IsBase(IBlock state) => state.RegistryId == this.Base.RegistryId
            || this.Replaceable.Contains(state.RegistryId);

        public bool PlaceBase(IWorldGenLevel level, Vector position)
        {
            if (this.Replaceable.Contains(level.GetBlock(position).RegistryId))
            {
                level.SetBlock(position, BlocksRegistry.GetDefault(this.Base.RegistryId));
                return true;
            }

            return false;
        }

        public void Grow(IWorldGenLevel level, Vector position, BlockFace direction, int height, bool merge)
        {
            if (!this.IsBase(level.GetBlock(position.Offset(direction.Opposite()))))
            {
                return;
            }

            for (var i = 0; i < height; i++)
            {
                var remaining = height - i;
                var thickness = remaining switch
                {
                    1 => merge ? "tip_merge" : "tip",
                    2 => "frustum",
                    _ => i == 0 ? "base" : "middle"
                };
                var state = BlocksRegistry.GetDefault(this.Pointed.RegistryId)
                    .WithProperty("vertical_direction", direction == BlockFace.Up ? "up" : "down")
                    .WithProperty("thickness", thickness);
                var water = FeatureHelpers.IsWaterFluid(level.GetBlock(position));
                state = state.WithProperty("waterlogged", water ? "true" : "false");
                level.SetBlock(position, state);
                position = position.Offset(direction);
            }
        }
    }
}
