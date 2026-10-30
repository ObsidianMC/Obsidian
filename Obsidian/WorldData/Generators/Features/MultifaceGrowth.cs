using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    private Feature MultifaceGrowth(JsonElement json)
    {
        var block = json.TryGetProperty("block", out var blockJson)
            ? BlocksRegistry.Get(blockJson.GetString()!)
            : BlocksRegistry.GlowLichen;
        var spreader = MultifaceSpreader.For(block);
        var supports = this.Blocks(json.GetProperty("can_be_placed_on"));
        var searchRange = json.TryGetProperty("search_range", out var range) ? range.GetInt32() : 10;
        var spreadChance = FloatOr(json, "chance_of_spreading", 0.5f);
        var valid = new List<BlockFace>();

        if (json.TryGetProperty("can_place_on_ceiling", out var ceiling) && ceiling.GetBoolean())
        {
            valid.Add(BlockFace.Up);
        }

        if (json.TryGetProperty("can_place_on_floor", out var floor) && floor.GetBoolean())
        {
            valid.Add(BlockFace.Down);
        }

        if (json.TryGetProperty("can_place_on_wall", out var wall) && wall.GetBoolean())
        {
            valid.AddRange(FeatureHelpers.Horizontal);
        }

        return (context, random, origin) =>
        {
            var level = context.Level;

            if (!SpeleothemEmpty(level.GetBlock(origin)))
            {
                return false;
            }

            var directions = valid.ToArray();
            FeatureHelpers.Shuffle(directions.AsSpan(), random);

            if (PlaceMultiface(level, random, origin, directions, spreader, supports, spreadChance))
            {
                return true;
            }

            foreach (var direction in directions)
            {
                var others = valid.Where(face => face != direction.Opposite()).ToArray();
                FeatureHelpers.Shuffle(others.AsSpan(), random);

                for (var i = 0; i < searchRange; i++)
                {
                    // 26.3 resets this probe to origin + direction on each iteration.
                    var position = origin.Offset(direction);
                    var state = level.GetBlock(position);

                    if (!SpeleothemEmpty(state) && state.RegistryId != block.RegistryId)
                    {
                        break;
                    }

                    if (PlaceMultiface(level, random, position, others, spreader, supports, spreadChance))
                    {
                        return true;
                    }
                }
            }

            return false;
        };
    }

    private static bool PlaceMultiface(
        IWorldGenLevel level,
        IRandomSource random,
        Vector position,
        BlockFace[] directions,
        MultifaceSpreader spreader,
        HashSet<int> supports,
        float spreadChance)
    {
        var existing = level.GetBlock(position);

        foreach (var direction in directions)
        {
            if (!supports.Contains(level.GetBlock(position.Offset(direction)).RegistryId))
            {
                continue;
            }

            var state = spreader.GetStateForPlacement(existing, level, position, direction);

            if (state is null)
            {
                return false;
            }

            level.SetBlock(position, state);
            level.MarkForPostProcessing(position);

            if (random.NextFloat() < spreadChance)
            {
                spreader.SpreadFromFaceTowardRandomDirection(
                    state, level, position, direction, random, markForPostProcessing: true);
            }

            return true;
        }

        return false;
    }
}
