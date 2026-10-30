using Obsidian.API.World.Generator.RandomSources;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features.Trees;

/// <summary>
/// 26.3's poplar crown: rhombus-shaped rows, narrowing over the top three, with random side holes, sideways logs
/// replacing the crown's leaves near the trunk and two small rows below the attachment. Ported from the 26.3 bytecode
/// for its row order, its corner cuts and its one float draw per edge-surviving position.
/// </summary>
internal sealed class PoplarFoliagePlacer(JsonElement json) : FoliagePlacer(json)
{
    private readonly IntProvider height = ValueProviders.Int(json.GetProperty("height"));
    private readonly float sideHoleChance = json.GetProperty("side_hole_chance").GetSingle();

    public override int FoliageHeight(IRandomSource random, int treeHeight) => this.height(random);

    protected override void CreateFoliage(
        TreePlacement tree, int freeTreeHeight, FoliageAttachment attachment, int foliageHeight, int foliageRadius,
        int offset)
    {
        var doubleTrunk = attachment.DoubleTrunk;
        var center = attachment.Position + (0, offset, 0);
        var radius = foliageRadius + attachment.RadiusOffset - 1;
        var flipped = tree.Random.NextBoolean();
        var top = foliageHeight;
        var shape = new CrownShape(top, flipped, doubleTrunk);

        this.PlaceRow(tree, center, radius - 2, top - 1, shape);
        this.PlaceRow(tree, center, radius - 1, top - 2, shape);
        this.PlaceRow(tree, center, radius - 1, top - 3, shape);

        for (var y = top - 4; y >= 1; y--)
        {
            this.PlaceRow(tree, center, radius, y, shape);
        }

        ReplaceLeavesWithLogs(tree, center, radius, top - 4, shape);

        var bottomRadius = Math.Clamp(radius - 2, 1, 2);

        this.PlaceRow(tree, center, radius - 1, 0, shape);
        this.PlaceRow(tree, center, bottomRadius, -1, shape);
    }

    protected override bool ShouldSkipLocation(
        IRandomSource random, int dx, int y, int dz, int radius, bool doubleTrunk) =>
        throw new InvalidOperationException("The poplar's rows need the crown's height and orientation.");

    private void PlaceRow(TreePlacement tree, Vector center, int radius, int y, CrownShape shape)
    {
        var extra = shape.DoubleTrunk ? 1 : 0;

        for (var dx = -radius; dx <= radius + extra; dx++)
        {
            for (var dz = -radius; dz <= radius + extra; dz++)
            {
                if (!this.ShouldSkip(tree.Random, dx, y, dz, radius, shape))
                {
                    TryPlaceLeaf(tree, center + (dx, y, dz));
                }
            }
        }
    }

    // The top two rows drop their edges; every other position draws whether a side hole cuts one more block.
    private bool ShouldSkip(IRandomSource random, int dx, int y, int dz, int radius, CrownShape shape)
    {
        var partial = shape.IsPartialRow(y);
        var cut = shape.CornerBlocksToCut(dx, dz, radius, partial);
        var distanceX = Math.Abs(dx);
        var distanceZ = Math.Abs(dz);
        var onEdge = distanceX == radius || distanceZ == radius;

        if (partial && onEdge)
        {
            return true;
        }

        var hole = random.NextFloat() <= this.sideHoleChance;
        var holeCut = hole ? 1 : 0;

        return !IsWithinRhombus(radius, distanceX, distanceZ, cut, holeCut);
    }

    // Inside the crown's rhombus, along the two axes through the trunk and at least four blocks from the edge,
    // leaves become logs lying along their axis (x on the trunk's own column).
    private static void ReplaceLeavesWithLogs(TreePlacement tree, Vector center, int radius, int y, CrownShape shape)
    {
        var extra = shape.DoubleTrunk ? 1 : 0;

        for (var dx = -radius; dx <= radius + extra; dx++)
        {
            for (var dz = -radius; dz <= radius + extra; dz++)
            {
                var distanceZ = Math.Abs(dz);
                var distanceX = Math.Abs(dx);
                var partial = shape.IsPartialRow(y);
                var cut = shape.CornerBlocksToCut(dx, dz, radius, partial);

                if (!IsWithinRhombus(radius, distanceX, distanceZ, cut, 2))
                {
                    continue;
                }

                var alongX = distanceZ == 0 && radius - distanceX >= 4;
                var alongZ = distanceX == 0 && radius - distanceZ >= 4;

                if (!alongX && !alongZ)
                {
                    continue;
                }

                var axis = distanceZ == 0 ? "x" : "z";

                TryPlaceLog(tree, center + (dx, y, dz), axis);
            }
        }
    }

    // Replaces the block only where it is exactly the state the foliage provider gives there.
    private static void TryPlaceLog(TreePlacement tree, Vector position, string axis)
    {
        var existing = tree.Level.GetBlock(position);
        var foliage = tree.FoliageState(position);

        if (!existing.IsSameState(foliage))
        {
            return;
        }

        var log = tree.TrunkState(position).WithProperty("axis", axis);
        tree.SetFoliage(position, log);
    }

    private static bool IsWithinRhombus(int radius, int distanceX, int distanceZ, int cut, int extraCut) =>
        distanceX + distanceZ <= radius * 2 - (cut + extraCut);

    private readonly record struct CrownShape(int Height, bool Flipped, bool DoubleTrunk)
    {
        /// <summary>The top two rows lose their edges.</summary>
        public bool IsPartialRow(int y) => this.Height - 1 == y || this.Height - 2 == y;

        // Two opposite quadrants (which two flips per tree) keep one more block; partial rows lose one more.
        public int CornerBlocksToCut(int dx, int dz, int radius, bool partial)
        {
            var corner = this.Flipped ? IsLeftTopOrRightLower(dx, dz) : IsLeftLowerOrRightTop(dx, dz);

            if (corner)
            {
                return radius - 1;
            }

            return partial ? radius + 1 : radius;
        }

        private static bool IsLeftLowerOrRightTop(int dx, int dz) => (dx > 0 && dz < 0) || (dx < 0 && dz > 0);

        private static bool IsLeftTopOrRightLower(int dx, int dz) => (dx > 0 && dz > 0) || (dx < 0 && dz < 0);
    }
}
