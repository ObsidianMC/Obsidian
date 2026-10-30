using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Density;
using System.IO;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

/// <summary>Tests a position of a level.</summary>
internal delegate bool BlockPredicate(IWorldGenLevel level, Vector position);

/// <summary>Tests a block an ore or processor would replace; 26.3's rule tests also see its position.</summary>
internal delegate bool RuleTest(IBlock block, Vector position, IRandomSource random);

internal sealed partial class FeatureRegistry
{
    public BlockPredicate Predicate(JsonElement json)
    {
        var type = json.GetProperty("type").GetString();
        var offset = Offset(json);

        switch (type)
        {
            case "minecraft:true":
                return static (_, _) => true;
            case "minecraft:matching_blocks":
            {
                var blocks = this.Blocks(json.GetProperty("blocks"));
                return (level, position) => blocks.Contains(level.GetBlock(position + offset).RegistryId);
            }
            case "minecraft:matching_block_tag":
            {
                var blocks = this.BlockTag(json.GetProperty("tag").GetString()!);
                return (level, position) => blocks.Contains(level.GetBlock(position + offset).RegistryId);
            }
            case "minecraft:matching_fluids":
            {
                var fluids = Fluids(json.GetProperty("fluids"));
                return (level, position) => fluids.Contains(level.GetBlock(position + offset).GetFluid());
            }
            case "minecraft:solid":
                return (level, position) => level.GetBlock(position + offset).IsSolid();
            case "minecraft:replaceable":
                return (level, position) => level.GetBlock(position + offset).CanBeReplaced();
            case "minecraft:would_survive":
            {
                var state = ReadBlock(json.GetProperty("state"));

                return (level, position) => this.CanSurvive(state, level, position + offset);
            }
            case "minecraft:has_sturdy_face":
            {
                var face = FeatureHelpers.ParseFace(json.GetProperty("direction").GetString());
                return (level, position) => level.GetBlock(position + offset).IsFaceSturdy(face);
            }
            case "minecraft:inside_world_bounds":
                return (level, position) => !level.IsOutsideBuildHeight(position.Y + offset.Y);
            case "minecraft:height_range":
                return HeightPredicate(json);
            case "minecraft:volume_match":
                return this.VolumeMatch(json);
            case "minecraft:not":
            {
                var predicate = this.Predicate(json.GetProperty("predicate"));
                return (level, position) => !predicate(level, position);
            }
            case "minecraft:all_of":
            {
                var predicates = json.GetProperty("predicates").EnumerateArray().Select(this.Predicate).ToArray();
                return (level, position) => predicates.All(predicate => predicate(level, position));
            }
            case "minecraft:any_of":
            {
                var predicates = json.GetProperty("predicates").EnumerateArray().Select(this.Predicate).ToArray();
                return (level, position) => predicates.Any(predicate => predicate(level, position));
            }
            default:
                throw new NotSupportedException($"The 26.3 block predicate {type} isn't implemented yet.");
        }
    }

    // 26.x support-sensitive blocks and the plants trees and large features grow from use 26.3's tags; other classes
    // keep the shared survival rules.
    private bool CanSurvive(IBlock state, IWorldGenLevel level, Vector position)
    {
        switch (state.BlockClass())
        {
            case "SnowLayerBlock":
                return this.SnowCanSurvive(level, position);
            case "SulfurSpikeBlock":
            case "PointedDripstoneBlock":
            {
                var direction = FeatureHelpers.ParseFace(state.GetProperty("vertical_direction"));
                var support = level.GetBlock(position.Offset(direction.Opposite()));
                var speleothems = this.BlockTag("minecraft:speleothems");

                return support.IsFaceSturdy(direction)
                    || speleothems.Contains(support.RegistryId)
                    && support.RegistryId == state.RegistryId
                    && support.GetProperty("vertical_direction") == state.GetProperty("vertical_direction");
            }
            case "ShelfMushroomBlock":
            {
                var facing = FeatureHelpers.ParseFace(state.GetProperty("facing"));
                var support = level.GetBlock(position.Offset(facing.Opposite()));

                return support.IsFaceSturdy(facing);
            }
            case "SaplingBlock":
            case "VegetationBlock":
                return this.BelowIn("minecraft:supports_vegetation", level, position);
            case "AzaleaBlock":
                return this.BelowIn("minecraft:supports_azalea", level, position);
            case "MangrovePropaguleBlock":
                return this.MangrovePropaguleCanSurvive(state, level, position);
            case "NetherFungusBlock":
            {
                var support = state.UnlocalizedName == "minecraft:crimson_fungus"
                    ? "minecraft:supports_crimson_fungus"
                    : "minecraft:supports_warped_fungus";

                return this.BelowIn(support, level, position);
            }
            case "MushroomBlock":
                return this.MushroomCanSurvive(level, position);
            default:
                return state.CanSurvive(level, position);
        }
    }

    private bool BelowIn(string tag, IWorldGenLevel level, Vector position)
    {
        var below = level.GetBlock(position + Vector.Down);

        return this.BlockTag(tag).Contains(below.RegistryId);
    }

    private bool SnowCanSurvive(IWorldGenLevel level, Vector position)
    {
        var below = level.GetBlock(position + Vector.Down);

        if (this.BlockTag("minecraft:cannot_support_snow_layer").Contains(below.RegistryId))
            return false;

        if (this.BlockTag("minecraft:support_override_snow_layer").Contains(below.RegistryId))
            return true;

        // SnowLayerBlock tests the collision face. Leaves have full collision despite not being sturdy support.
        return below.IsCollisionShapeFullBlock() || below.IsFaceSturdy(BlockFace.Up)
            || below.Is(BlockIds.Snow) && below.GetProperty("layers") == "8";
    }

    // A hanging propagule needs its supporting leaves above; a planted one is a sapling on its own support tag.
    private bool MangrovePropaguleCanSurvive(IBlock state, IWorldGenLevel level, Vector position)
    {
        if (state.GetProperty("hanging") != "true")
        {
            return this.BelowIn("minecraft:supports_mangrove_propagule", level, position);
        }

        var above = level.GetBlock(position + Vector.Up);
        var supports = this.BlockTag("minecraft:supports_hanging_mangrove_propagule");

        return supports.Contains(above.RegistryId);
    }

    // Before lighting, sky-lit dimensions report 15 even underground. The Nether reports 0.
    private bool MushroomCanSurvive(IWorldGenLevel level, Vector position)
    {
        var rawBrightness = level.UnlitBrightness;

        if (this.BelowIn("minecraft:overrides_mushroom_light_requirement", level, position))
        {
            return true;
        }

        var below = level.GetBlock(position + Vector.Down);

        return rawBrightness < 13 && below.IsSolid() && below.IsCollisionShapeFullBlock();
    }

    public RuleTest Rule(JsonElement json)
    {
        var type = json.GetProperty("predicate_type").GetString();

        switch (type)
        {
            case "minecraft:always_true":
                return static (_, _, _) => true;
            case "minecraft:block_match":
            {
                var block = ReadBlock(json.GetProperty("block")).RegistryId;
                return (state, _, _) => state.RegistryId == block;
            }
            case "minecraft:blockstate_match":
            {
                var block = ReadBlock(json.GetProperty("block_state"));
                return (state, _, _) => state.IsSameState(block);
            }
            case "minecraft:tag_match":
            {
                var blocks = this.BlockTag(json.GetProperty("tag").GetString()!);
                return (state, _, _) => blocks.Contains(state.RegistryId);
            }
            case "minecraft:random_block_match":
            {
                var block = ReadBlock(json.GetProperty("block")).RegistryId;
                var probability = json.GetProperty("probability").GetSingle();

                // Draws only for a matching block.
                return (state, _, random) => state.RegistryId == block && random.NextFloat() < probability;
            }
            case "minecraft:height_match":
            {
                var min = json.GetProperty("min_inclusive").GetInt32();
                var max = json.GetProperty("max_inclusive").GetInt32();
                return (_, position, _) => position.Y >= min && position.Y <= max;
            }
            case "minecraft:not":
            {
                var rule = this.Rule(json.GetProperty("rule"));
                return (state, position, random) => !rule(state, position, random);
            }
            case "minecraft:all_of":
            {
                var rules = json.GetProperty("rules").EnumerateArray().Select(this.Rule).ToArray();
                return (state, position, random) => rules.All(rule => rule(state, position, random));
            }
            case "minecraft:any_of":
            {
                var rules = json.GetProperty("rules").EnumerateArray().Select(this.Rule).ToArray();
                return (state, position, random) => rules.Any(rule => rule(state, position, random));
            }
            default:
                throw new NotSupportedException($"The 26.3 rule test {type} isn't implemented yet.");
        }
    }

    /// <summary>Block registry ids of a holder set: an identifier, a <c>#tag</c> or a list of either.</summary>
    public HashSet<int> Blocks(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
        {
            var result = new HashSet<int>();

            foreach (var entry in json.EnumerateArray())
            {
                result.UnionWith(this.Blocks(entry));
            }

            return result;
        }

        var name = json.GetString()!;

        if (name.StartsWith('#'))
        {
            return this.BlockTag(name[1..]);
        }

        return [ReadBlock(json).RegistryId];
    }

    private static HashSet<FluidKind> Fluids(JsonElement json)
    {
        var names = json.ValueKind == JsonValueKind.Array
            ? json.EnumerateArray().Select(entry => WorldgenData.Qualify(entry.GetString()!)).ToArray()
            : [WorldgenData.Qualify(json.GetString()!)];
        var result = new HashSet<FluidKind>();

        foreach (var name in names)
        {
            var fluid = name switch
            {
                "minecraft:water" => FluidKind.Water,
                "minecraft:flowing_water" => FluidKind.FlowingWater,
                "minecraft:lava" => FluidKind.Lava,
                "minecraft:flowing_lava" => FluidKind.FlowingLava,
                "minecraft:empty" => FluidKind.Empty,
                _ => throw new InvalidDataException($"Unknown fluid {name}.")
            };
            result.Add(fluid);
        }

        return result;
    }

    private static Vector Offset(JsonElement json)
    {
        if (!json.TryGetProperty("offset", out var offset))
        {
            return Vector.Zero;
        }

        return new Vector(offset[0].GetInt32(), offset[1].GetInt32(), offset[2].GetInt32());
    }
}
