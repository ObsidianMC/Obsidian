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
            ? ReadBlock(blockJson)
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

        var validFaces = valid.ToArray();

        return (context, random, origin) =>
        {
            var level = context.Level;

            if (!SpeleothemEmpty(level.GetBlock(origin)))
            {
                return false;
            }

            // At most the six faces, so both lists live on the stack.
            Span<BlockFace> directions = stackalloc BlockFace[validFaces.Length];
            validFaces.CopyTo(directions);
            FeatureHelpers.Shuffle(directions, random);

            if (PlaceMultiface(level, random, origin, directions, spreader, supports, spreadChance))
            {
                return true;
            }

            Span<BlockFace> othersBuffer = stackalloc BlockFace[validFaces.Length];
            foreach (var direction in directions)
            {
                var others = OtherFaces(validFaces, direction.Opposite(), othersBuffer);
                FeatureHelpers.Shuffle(others, random);

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

    // The faces other than the excluded one, in their order, written to the start of the buffer.
    private static Span<BlockFace> OtherFaces(BlockFace[] faces, BlockFace excluded, Span<BlockFace> buffer)
    {
        var count = 0;
        foreach (var face in faces)
        {
            if (face != excluded)
                buffer[count++] = face;
        }

        return buffer[..count];
    }

    private static bool PlaceMultiface(
        IWorldGenLevel level,
        IRandomSource random,
        Vector position,
        ReadOnlySpan<BlockFace> directions,
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
