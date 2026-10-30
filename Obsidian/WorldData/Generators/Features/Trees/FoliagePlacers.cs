using Obsidian.API.World.Generator.RandomSources;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features.Trees;

/// <summary>Oak and birch: rows shrinking toward the top, with random corners.</summary>
internal sealed class BlobFoliagePlacer(JsonElement json) : FoliagePlacer(json)
{
    private readonly int height = json.GetProperty("height").GetInt32();

    public override int FoliageHeight(IRandomSource random, int treeHeight) => this.height;

    protected override void CreateFoliage(
        TreePlacement tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight, int foliageRadius,
        int offset)
    {
        for (var y = offset; y >= offset - foliageHeight; y--)
        {
            var radius = Math.Max(foliageRadius + attachment.RadiusOffset - 1 - y / 2, 0);
            this.PlaceLeavesRow(tree, attachment.Position, radius, y, attachment.DoubleTrunk);
        }
    }

    protected override bool ShouldSkipLocation(
        IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        dx == radius && dz == radius && (random.NextInt(2) == 0 || y == 0);
}

internal sealed class BushFoliagePlacer(JsonElement json) : FoliagePlacer(json)
{
    private readonly int height = json.GetProperty("height").GetInt32();

    public override int FoliageHeight(IRandomSource random, int treeHeight) => this.height;

    protected override void CreateFoliage(
        TreePlacement tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight, int foliageRadius,
        int offset)
    {
        for (var y = offset; y >= offset - foliageHeight; y--)
        {
            var radius = foliageRadius + attachment.RadiusOffset - 1 - y;
            this.PlaceLeavesRow(tree, attachment.Position, radius, y, attachment.DoubleTrunk);
        }
    }

    protected override bool ShouldSkipLocation(
        IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        dx == radius && dz == radius && random.NextInt(2) == 0;
}

/// <summary>Fancy oak's round clusters, one block wider in the middle rows.</summary>
internal sealed class FancyFoliagePlacer(JsonElement json) : FoliagePlacer(json)
{
    private readonly int height = json.GetProperty("height").GetInt32();

    public override int FoliageHeight(IRandomSource random, int treeHeight) => this.height;

    protected override void CreateFoliage(
        TreePlacement tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight, int foliageRadius,
        int offset)
    {
        for (var y = offset; y >= offset - foliageHeight; y--)
        {
            var middle = y != offset && y != offset - foliageHeight;
            var radius = foliageRadius + (middle ? 1 : 0);
            this.PlaceLeavesRow(tree, attachment.Position, radius, y, attachment.DoubleTrunk);
        }
    }

    protected override bool ShouldSkipLocation(
        IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk)
    {
        var x = dx + 0.5f;
        var z = dz + 0.5f;

        return x * x + z * z > radius * radius;
    }
}

/// <summary>Jungle trees and their branches: rows widening downward, one or two for single trunks.</summary>
internal sealed class JungleFoliagePlacer(JsonElement json) : FoliagePlacer(json)
{
    private readonly int height = json.GetProperty("height").GetInt32();

    public override int FoliageHeight(IRandomSource random, int treeHeight) => this.height;

    protected override void CreateFoliage(
        TreePlacement tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight, int foliageRadius,
        int offset)
    {
        var rows = attachment.DoubleTrunk ? foliageHeight : 1 + tree.Random.NextInt(2);

        for (var y = offset; y >= offset - rows; y--)
        {
            var radius = foliageRadius + attachment.RadiusOffset + 1 - y;
            this.PlaceLeavesRow(tree, attachment.Position, radius, y, attachment.DoubleTrunk);
        }
    }

    protected override bool ShouldSkipLocation(
        IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        dx + dz >= 7 || dx * dx + dz * dz > radius * radius;
}

/// <summary>Spruce: rows alternating between growing and resetting radii.</summary>
internal sealed class SpruceFoliagePlacer(JsonElement json) : FoliagePlacer(json)
{
    private readonly IntProvider trunkHeight = ValueProviders.Int(json.GetProperty("trunk_height"));

    public override int FoliageHeight(IRandomSource random, int treeHeight) =>
        Math.Max(4, treeHeight - this.trunkHeight(random));

    protected override void CreateFoliage(
        TreePlacement tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight, int foliageRadius,
        int offset)
    {
        var radius = tree.Random.NextInt(2);
        var maxRadius = 1;
        var resetRadius = 0;

        for (var y = offset; y >= -foliageHeight; y--)
        {
            this.PlaceLeavesRow(tree, attachment.Position, radius, y, attachment.DoubleTrunk);

            if (radius >= maxRadius)
            {
                radius = resetRadius;
                resetRadius = 1;
                maxRadius = Math.Min(maxRadius + 1, foliageRadius + attachment.RadiusOffset);
            }
            else
            {
                radius++;
            }
        }
    }

    protected override bool ShouldSkipLocation(
        IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        dx == radius && dz == radius && radius > 0;
}

/// <summary>Pine: a narrow cone, its radius growing with a draw from the trunk height.</summary>
internal sealed class PineFoliagePlacer(JsonElement json) : FoliagePlacer(json)
{
    private readonly IntProvider height = ValueProviders.Int(json.GetProperty("height"));

    public override int FoliageHeight(IRandomSource random, int treeHeight) => this.height(random);

    public override int FoliageRadius(IRandomSource random, int trunkHeight)
    {
        var radius = base.FoliageRadius(random, trunkHeight);

        return radius + random.NextInt(Math.Max(trunkHeight + 1, 1));
    }

    protected override void CreateFoliage(
        TreePlacement tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight, int foliageRadius,
        int offset)
    {
        var radius = 0;

        for (var y = offset; y >= offset - foliageHeight; y--)
        {
            this.PlaceLeavesRow(tree, attachment.Position, radius, y, attachment.DoubleTrunk);

            if (radius >= 1 && y == offset - foliageHeight + 1)
            {
                radius--;
            }
            else if (radius < foliageRadius + attachment.RadiusOffset)
            {
                radius++;
            }
        }
    }

    protected override bool ShouldSkipLocation(
        IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        dx == radius && dz == radius && radius > 0;
}

/// <summary>Mega spruce and pine: a crown widening in steps toward its base.</summary>
internal sealed class MegaPineFoliagePlacer(JsonElement json) : FoliagePlacer(json)
{
    private readonly IntProvider crownHeight = ValueProviders.Int(json.GetProperty("crown_height"));

    public override int FoliageHeight(IRandomSource random, int treeHeight) => this.crownHeight(random);

    protected override void CreateFoliage(
        TreePlacement tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight, int foliageRadius,
        int offset)
    {
        var center = attachment.Position;
        var previousRadius = 0;

        for (var y = center.Y - foliageHeight + offset; y <= center.Y + offset; y++)
        {
            var depth = center.Y - y;
            var widening = Mth.Floor((float)depth / foliageHeight * 3.5f);
            var radius = foliageRadius + attachment.RadiusOffset + widening;
            var bump = depth > 0 && radius == previousRadius && (y & 1) == 0;
            var rowRadius = bump ? radius + 1 : radius;

            this.PlaceLeavesRow(tree, new Vector(center.X, y, center.Z), rowRadius, 0, attachment.DoubleTrunk);
            previousRadius = radius;
        }
    }

    protected override bool ShouldSkipLocation(
        IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        dx + dz >= 7 || dx * dx + dz * dz > radius * radius;
}

internal sealed class AcaciaFoliagePlacer(JsonElement json) : FoliagePlacer(json)
{
    public override int FoliageHeight(IRandomSource random, int treeHeight) => 0;

    protected override void CreateFoliage(
        TreePlacement tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight, int foliageRadius,
        int offset)
    {
        var doubleTrunk = attachment.DoubleTrunk;
        var center = attachment.Position + (0, offset, 0);
        var wideRadius = foliageRadius + attachment.RadiusOffset;

        this.PlaceLeavesRow(tree, center, wideRadius, -1 - foliageHeight, doubleTrunk);
        this.PlaceLeavesRow(tree, center, foliageRadius - 1, -foliageHeight, doubleTrunk);
        this.PlaceLeavesRow(tree, center, wideRadius - 1, 0, doubleTrunk);
    }

    protected override bool ShouldSkipLocation(
        IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk)
    {
        if (y == 0)
        {
            return (dx > 1 || dz > 1) && dx != 0 && dz != 0;
        }

        return dx == radius && dz == radius && radius > 0;
    }
}

internal sealed class DarkOakFoliagePlacer(JsonElement json) : FoliagePlacer(json)
{
    public override int FoliageHeight(IRandomSource random, int treeHeight) => 4;

    protected override void CreateFoliage(
        TreePlacement tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight, int foliageRadius,
        int offset)
    {
        var center = attachment.Position + (0, offset, 0);
        var doubleTrunk = attachment.DoubleTrunk;

        if (!doubleTrunk)
        {
            this.PlaceLeavesRow(tree, center, foliageRadius + 2, -1, doubleTrunk);
            this.PlaceLeavesRow(tree, center, foliageRadius + 1, 0, doubleTrunk);
            return;
        }

        this.PlaceLeavesRow(tree, center, foliageRadius + 2, -1, doubleTrunk);
        this.PlaceLeavesRow(tree, center, foliageRadius + 3, 0, doubleTrunk);
        this.PlaceLeavesRow(tree, center, foliageRadius + 2, 1, doubleTrunk);

        if (tree.Random.NextBoolean())
        {
            this.PlaceLeavesRow(tree, center, foliageRadius, 2, doubleTrunk);
        }
    }

    // The widest row of a double trunk skips its four outer corners.
    protected override bool ShouldSkipLocationSigned(
        IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk)
    {
        var cornerX = dx == -radius || dx >= radius;
        var cornerZ = dz == -radius || dz >= radius;

        if (y == 0 && doubleTrunk && cornerX && cornerZ)
        {
            return true;
        }

        return base.ShouldSkipLocationSigned(random, dx, y, dz, radius, doubleTrunk);
    }

    protected override bool ShouldSkipLocation(
        IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk)
    {
        if (y == -1 && !doubleTrunk)
        {
            return dx == radius && dz == radius;
        }

        return y == 1 && dx + dz > radius * 2 - 2;
    }
}

/// <summary>Azalea: leaves at random offsets around each attachment.</summary>
internal sealed class RandomSpreadFoliagePlacer(JsonElement json) : FoliagePlacer(json)
{
    private readonly IntProvider foliageHeight = ValueProviders.Int(json.GetProperty("foliage_height"));
    private readonly int leafPlacementAttempts = json.GetProperty("leaf_placement_attempts").GetInt32();

    public override int FoliageHeight(IRandomSource random, int treeHeight) => this.foliageHeight(random);

    protected override void CreateFoliage(
        TreePlacement tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight, int foliageRadius,
        int offset)
    {
        var random = tree.Random;

        for (var i = 0; i < this.leafPlacementAttempts; i++)
        {
            var dx = random.NextInt(foliageRadius) - random.NextInt(foliageRadius);
            var dy = random.NextInt(foliageHeight) - random.NextInt(foliageHeight);
            var dz = random.NextInt(foliageRadius) - random.NextInt(foliageRadius);

            TryPlaceLeaf(tree, attachment.Position + (dx, dy, dz));
        }
    }

    protected override bool ShouldSkipLocation(
        IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        false;
}

/// <summary>Cherry: a wide crown with holes in its corners and leaves hanging below.</summary>
internal sealed class CherryFoliagePlacer(JsonElement json) : FoliagePlacer(json)
{
    private readonly IntProvider height = ValueProviders.Int(json.GetProperty("height"));
    private readonly float wideBottomLayerHoleChance = json.GetProperty("wide_bottom_layer_hole_chance").GetSingle();
    private readonly float cornerHoleChance = json.GetProperty("corner_hole_chance").GetSingle();
    private readonly float hangingLeavesChance = json.GetProperty("hanging_leaves_chance").GetSingle();

    private readonly float hangingLeavesExtensionChance =
        json.GetProperty("hanging_leaves_extension_chance").GetSingle();

    public override int FoliageHeight(IRandomSource random, int treeHeight) => this.height(random);

    protected override void CreateFoliage(
        TreePlacement tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight, int foliageRadius,
        int offset)
    {
        var doubleTrunk = attachment.DoubleTrunk;
        var center = attachment.Position + (0, offset, 0);
        var radius = foliageRadius + attachment.RadiusOffset - 1;
        var hanging = this.hangingLeavesChance;
        var extension = this.hangingLeavesExtensionChance;

        this.PlaceLeavesRow(tree, center, radius - 2, foliageHeight - 3, doubleTrunk);
        this.PlaceLeavesRow(tree, center, radius - 1, foliageHeight - 4, doubleTrunk);

        for (var y = foliageHeight - 5; y >= 0; y--)
        {
            this.PlaceLeavesRow(tree, center, radius, y, doubleTrunk);
        }

        this.PlaceLeavesRowWithHangingLeavesBelow(tree, center, radius, -1, doubleTrunk, hanging, extension);
        this.PlaceLeavesRowWithHangingLeavesBelow(tree, center, radius - 1, -2, doubleTrunk, hanging, extension);
    }

    protected override bool ShouldSkipLocation(
        IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk)
    {
        var bottomEdge = y == -1 && (dx == radius || dz == radius);

        if (bottomEdge && random.NextFloat() < this.wideBottomLayerHoleChance)
        {
            return true;
        }

        var isCorner = dx == radius && dz == radius;

        if (radius > 2)
        {
            return isCorner || (dx + dz > radius * 2 - 2 && random.NextFloat() < this.cornerHoleChance);
        }

        return isCorner && random.NextFloat() < this.cornerHoleChance;
    }
}
