using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.IO;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features.Trees;

/// <summary>
/// Mangrove roots: the trunk starts above the origin, and four roots per side are simulated outward and down before
/// any is placed. A root fails the whole tree when it can't end within its length.
/// </summary>
internal sealed class MangroveRootPlacer
{
    private readonly IntProvider trunkOffsetY;
    private readonly StateProvider rootProvider;
    private readonly StateProvider? aboveRootProvider;
    private readonly float aboveRootPlacementChance;
    private readonly HashSet<int> canGrowThrough;
    private readonly HashSet<int> muddyRootsIn;
    private readonly StateProvider muddyRootsProvider;
    private readonly int maxRootWidth;
    private readonly int maxRootLength;
    private readonly float randomSkewChance;

    private MangroveRootPlacer(FeatureRegistry registry, JsonElement json)
    {
        this.trunkOffsetY = ValueProviders.Int(json.GetProperty("trunk_offset_y"));
        this.rootProvider = registry.Provider(json.GetProperty("root_provider"));

        if (json.TryGetProperty("above_root_placement", out var above))
        {
            this.aboveRootProvider = registry.Provider(above.GetProperty("above_root_provider"));
            this.aboveRootPlacementChance = above.GetProperty("above_root_placement_chance").GetSingle();
        }

        var placement = json.GetProperty("mangrove_root_placement");

        this.canGrowThrough = registry.Blocks(placement.GetProperty("can_grow_through"));
        this.muddyRootsIn = registry.Blocks(placement.GetProperty("muddy_roots_in"));
        this.muddyRootsProvider = registry.Provider(placement.GetProperty("muddy_roots_provider"));
        this.maxRootWidth = placement.GetProperty("max_root_width").GetInt32();
        this.maxRootLength = placement.GetProperty("max_root_length").GetInt32();
        this.randomSkewChance = placement.GetProperty("random_skew_chance").GetSingle();
    }

    public static MangroveRootPlacer Read(FeatureRegistry registry, JsonElement json)
    {
        var type = json.GetProperty("type").GetString();

        if (type != "minecraft:mangrove_root_placer")
        {
            throw new InvalidDataException($"Unknown root placer {type}.");
        }

        return new MangroveRootPlacer(registry, json);
    }

    public Vector TrunkOrigin(Vector origin, IRandomSource random) => origin + (0, this.trunkOffsetY(random), 0);

    public bool PlaceRoots(TreePlacement tree, Vector origin, Vector trunkOrigin)
    {
        for (var position = origin; position.Y < trunkOrigin.Y; position += Vector.Up)
        {
            if (!this.CanPlaceRoot(tree, position))
            {
                return false;
            }
        }

        var positions = new List<Vector> { trunkOrigin + Vector.Down };

        foreach (var direction in FeatureHelpers.Horizontal)
        {
            var start = trunkOrigin.Offset(direction);
            var root = new List<Vector>();

            if (!this.SimulateRoots(tree, start, direction, trunkOrigin, root, 0))
            {
                return false;
            }

            positions.AddRange(root);
            positions.Add(start);
        }

        foreach (var position in positions)
        {
            this.PlaceRoot(tree, position);
        }

        return true;
    }

    private bool CanPlaceRoot(TreePlacement tree, Vector position)
    {
        var growsThrough = this.canGrowThrough.Contains(tree.Level.GetBlock(position).RegistryId);

        return tree.Tags.ValidTreePos(tree.Level, position) || growsThrough;
    }

    private void PlaceRoot(TreePlacement tree, Vector position)
    {
        var level = tree.Level;

        if (this.muddyRootsIn.Contains(level.GetBlock(position).RegistryId))
        {
            var muddy = FeatureRegistry.State(this.muddyRootsProvider, level, tree.Random, position);
            tree.SetRoot(position, Waterlogged(level, position, muddy));
            return;
        }

        if (!this.CanPlaceRoot(tree, position))
        {
            return;
        }

        var root = FeatureRegistry.State(this.rootProvider, level, tree.Random, position);
        tree.SetRoot(position, Waterlogged(level, position, root));

        if (this.aboveRootProvider is null)
        {
            return;
        }

        var abovePosition = position + Vector.Up;
        var placeAbove = tree.Random.NextFloat() < this.aboveRootPlacementChance;

        if (placeAbove && level.GetBlock(abovePosition).IsAir)
        {
            var above = FeatureRegistry.State(this.aboveRootProvider, level, tree.Random, abovePosition);
            tree.SetRoot(abovePosition, Waterlogged(level, abovePosition, above));
        }
    }

    // Vanilla's getPotentiallyWaterloggedState: waterlogged in any water, still or flowing.
    private static IBlock Waterlogged(IWorldGenLevel level, Vector position, IBlock block)
    {
        if (!block.HasProperty("waterlogged"))
        {
            return block;
        }

        var fluid = level.GetBlock(position).GetFluid();
        var water = fluid is FluidKind.Water or FluidKind.FlowingWater;

        return block.WithProperty("waterlogged", water);
    }

    private bool SimulateRoots(
        TreePlacement tree, Vector position, BlockFace direction, Vector trunkOrigin, List<Vector> root, int depth)
    {
        if (depth == this.maxRootLength || root.Count > this.maxRootLength)
        {
            return false;
        }

        foreach (var next in this.PotentialRootPositions(position, direction, tree.Random, trunkOrigin))
        {
            if (!this.CanPlaceRoot(tree, next))
            {
                continue;
            }

            root.Add(next);

            if (!this.SimulateRoots(tree, next, direction, trunkOrigin, root, depth + 1))
            {
                return false;
            }
        }

        return true;
    }

    private Vector[] PotentialRootPositions(Vector position, BlockFace direction, IRandomSource random, Vector trunk)
    {
        var below = position + Vector.Down;
        var outward = position.Offset(direction);
        var offset = position - trunk;
        var distance = Math.Abs(offset.X) + Math.Abs(offset.Y) + Math.Abs(offset.Z);
        var maxWidth = this.maxRootWidth;

        if (distance > maxWidth - 3 && distance <= maxWidth)
        {
            return random.NextFloat() < this.randomSkewChance ? [below, outward + Vector.Down] : [below];
        }

        if (distance > maxWidth)
        {
            return [below];
        }

        if (random.NextFloat() < this.randomSkewChance)
        {
            return [below];
        }

        return random.NextBoolean() ? [outward] : [below];
    }
}
