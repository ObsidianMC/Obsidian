using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Text.Json;
using ApiFoliagePlacer = Obsidian.API.World.Features.Trees.FoliagePlacer;

namespace Obsidian.WorldData.Generators.Features.Trees;

/// <summary>Places a tree's leaves around its foliage attachments, like vanilla's <c>FoliagePlacer</c>.</summary>
internal abstract class FoliagePlacer
{
    private readonly IntProvider radius;
    private readonly IntProvider offset;

    /// <summary>Reads the <c>radius</c> and <c>offset</c> every foliage placer has.</summary>
    protected FoliagePlacer(FeatureRegistry registry, JsonElement json)
        : this(registry.Int(json.GetProperty("radius")), registry.Int(json.GetProperty("offset")))
    {
    }

    protected FoliagePlacer(IntProvider radius, IntProvider offset)
    {
        this.radius = radius;
        this.offset = offset;
    }

    public static FoliagePlacer Read(FeatureRegistry registry, JsonElement json)
    {
        var type = json.GetProperty("type").GetString();

        return type switch
        {
            "minecraft:blob_foliage_placer" => new BlobFoliagePlacer(registry, json),
            "minecraft:bush_foliage_placer" => new BushFoliagePlacer(registry, json),
            "minecraft:fancy_foliage_placer" => new FancyFoliagePlacer(registry, json),
            "minecraft:jungle_foliage_placer" => new JungleFoliagePlacer(registry, json),
            "minecraft:spruce_foliage_placer" => new SpruceFoliagePlacer(registry, json),
            "minecraft:pine_foliage_placer" => new PineFoliagePlacer(registry, json),
            "minecraft:mega_pine_foliage_placer" => new MegaPineFoliagePlacer(registry, json),
            "minecraft:acacia_foliage_placer" => new AcaciaFoliagePlacer(registry, json),
            "minecraft:dark_oak_foliage_placer" => new DarkOakFoliagePlacer(registry, json),
            "minecraft:random_spread_foliage_placer" => new RandomSpreadFoliagePlacer(registry, json),
            "minecraft:cherry_foliage_placer" => new CherryFoliagePlacer(registry, json),
            "minecraft:poplar_foliage_placer" => new PoplarFoliagePlacer(registry, json),
            _ => new PluginFoliagePlacer(registry.PluginValue<ApiFoliagePlacer>(json, type, "foliage placer"))
        };
    }

    /// <summary>Draws the offset, then places the foliage.</summary>
    public void CreateFoliage(
        TreePlacement tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight, int foliageRadius)
    {
        var sampledOffset = this.offset(tree.Random);

        this.CreateFoliage(tree, freeTreeHeight, attachment, foliageHeight, foliageRadius, sampledOffset);
    }

    public abstract int FoliageHeight(IRandomSource random, int treeHeight);

    public virtual int FoliageRadius(IRandomSource random, int trunkHeight) => this.radius(random);

    protected abstract void CreateFoliage(
        TreePlacement tree,
        int freeTreeHeight,
        FoliageAttachment attachment,
        int foliageHeight,
        int foliageRadius,
        int offset);

    /// <summary>Whether a row skips the position, from its distances to the attachment.</summary>
    protected abstract bool ShouldSkipLocation(
        IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk);

    /// <summary>A double trunk measures from the nearer of its two columns on each axis.</summary>
    protected virtual bool ShouldSkipLocationSigned(
        IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk)
    {
        var distanceX = doubleTrunk ? Math.Min(Math.Abs(dx), Math.Abs(dx - 1)) : Math.Abs(dx);
        var distanceZ = doubleTrunk ? Math.Min(Math.Abs(dz), Math.Abs(dz - 1)) : Math.Abs(dz);

        return this.ShouldSkipLocation(random, distanceX, y, distanceZ, radius, doubleTrunk);
    }

    protected void PlaceLeavesRow(TreePlacement tree, Vector center, int radius, int y, bool doubleTrunk)
    {
        var extra = doubleTrunk ? 1 : 0;

        for (var dx = -radius; dx <= radius + extra; dx++)
        {
            for (var dz = -radius; dz <= radius + extra; dz++)
            {
                if (!this.ShouldSkipLocationSigned(tree.Random, dx, y, dz, radius, doubleTrunk))
                {
                    TryPlaceLeaf(tree, center + (dx, y, dz));
                }
            }
        }
    }

    protected void PlaceLeavesRowWithHangingLeavesBelow(
        TreePlacement tree,
        Vector center,
        int radius,
        int y,
        bool doubleTrunk,
        float hangingLeavesChance,
        float hangingLeavesExtensionChance)
    {
        this.PlaceLeavesRow(tree, center, radius, y, doubleTrunk);

        var extra = doubleTrunk ? 1 : 0;
        var below = center + Vector.Down;

        foreach (var direction in FeatureHelpers.Horizontal)
        {
            var clockWise = direction.ClockWise();
            var positive = clockWise is BlockFace.South or BlockFace.East;
            var sideOffset = positive ? radius + extra : radius;
            var step = Vector.Zero.Offset(direction);
            var cursor = center + (0, y - 1, 0) + Vector.Zero.Offset(clockWise) * sideOffset + step * -radius;

            for (var i = -radius; i < radius + extra; i++)
            {
                var hasLeafAbove = tree.IsFoliageSet(cursor + Vector.Up);

                if (hasLeafAbove && TryPlaceExtension(tree, hangingLeavesChance, below, cursor))
                {
                    TryPlaceExtension(tree, hangingLeavesExtensionChance, below, cursor + Vector.Down);
                }

                cursor += step;
            }
        }
    }

    /// <summary>
    /// Vanilla <c>tryPlaceLeaf</c>: places a foliage state at a valid tree position that doesn't hold persistent
    /// leaves, waterlogged where a water source was.
    /// </summary>
    protected static bool TryPlaceLeaf(TreePlacement tree, Vector position)
    {
        var existing = tree.Level.GetBlock(position);
        var persistent = existing.GetProperty("persistent") == "true";

        if (persistent || !tree.Tags.ValidTreePos(tree.Level, position))
        {
            return false;
        }

        var leaf = tree.FoliageState(position);

        if (leaf.HasProperty("waterlogged"))
        {
            var waterSource = tree.Level.GetBlock(position).GetFluid() == FluidKind.Water;
            leaf = leaf.WithProperty("waterlogged", waterSource);
        }

        tree.SetFoliage(position, leaf);
        return true;
    }

    private static bool TryPlaceExtension(TreePlacement tree, float chance, Vector origin, Vector position)
    {
        var difference = position - origin;
        var distance = Math.Abs(difference.X) + Math.Abs(difference.Y) + Math.Abs(difference.Z);

        if (distance >= 7)
        {
            return false;
        }

        if (tree.Random.NextFloat() > chance)
        {
            return false;
        }

        return TryPlaceLeaf(tree, position);
    }
}
