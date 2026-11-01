using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Text.Json;
using ApiTrunkPlacer = Obsidian.API.World.Features.Trees.TrunkPlacer;

namespace Obsidian.WorldData.Generators.Features.Trees;

/// <summary>
/// Where foliage grows: the top of a trunk or branch, the radius change and whether the trunk is 2x2.
/// </summary>
internal readonly record struct FoliageAttachment(Vector Position, int RadiusOffset, bool DoubleTrunk);

/// <summary>Places a tree's trunk and reports where its foliage attaches, like vanilla's <c>TrunkPlacer</c>.</summary>
internal abstract class TrunkPlacer(JsonElement json)
{
    private readonly int baseHeight = json.GetProperty("base_height").GetInt32();
    private readonly int heightRandA = json.GetProperty("height_rand_a").GetInt32();
    private readonly int heightRandB = json.GetProperty("height_rand_b").GetInt32();

    public static TrunkPlacer Read(FeatureRegistry registry, JsonElement json)
    {
        var type = json.GetProperty("type").GetString();

        return type switch
        {
            "minecraft:straight_trunk_placer" => new StraightTrunkPlacer(json),
            "minecraft:giant_trunk_placer" => new GiantTrunkPlacer(json),
            "minecraft:mega_jungle_trunk_placer" => new MegaJungleTrunkPlacer(json),
            "minecraft:forking_trunk_placer" => new ForkingTrunkPlacer(json),
            "minecraft:bending_trunk_placer" => new BendingTrunkPlacer(registry, json),
            "minecraft:dark_oak_trunk_placer" => new DarkOakTrunkPlacer(json),
            "minecraft:poplar_trunk_placer" => new PoplarTrunkPlacer(registry, json),
            "minecraft:fancy_trunk_placer" => new FancyTrunkPlacer(json),
            "minecraft:cherry_trunk_placer" => new CherryTrunkPlacer(registry, json),
            "minecraft:upwards_branching_trunk_placer" => new UpwardsBranchingTrunkPlacer(registry, json),
            _ => new PluginTrunkPlacer(registry.PluginValue<ApiTrunkPlacer>(json, type, "trunk placer"), json)
        };
    }

    /// <summary>Vanilla <c>getTreeHeight</c>: the base height plus two uniform draws.</summary>
    public int TreeHeight(IRandomSource random)
    {
        var extraA = random.NextInt(this.heightRandA + 1);
        var extraB = random.NextInt(this.heightRandB + 1);

        return this.baseHeight + extraA + extraB;
    }

    /// <summary>Places the trunk from the origin and returns the foliage attachments.</summary>
    /// <param name="freeTreeHeight">The height available, possibly clipped below the sampled height.</param>
    public abstract List<FoliageAttachment> PlaceTrunk(TreePlacement tree, int freeTreeHeight, Vector origin);

    /// <summary>Whether a trunk may grow through the position: a valid tree position or an existing log.</summary>
    public bool IsFree(TreeTags tags, IWorldGenLevel level, Vector position) =>
        this.ValidTreePos(tags, level, position) || tags.IsLog(level.GetBlock(position));

    protected virtual bool ValidTreePos(TreeTags tags, IWorldGenLevel level, Vector position) =>
        tags.ValidTreePos(level, position);

    /// <summary>Places a trunk block at a valid tree position, optionally changing the provided state.</summary>
    protected bool PlaceLog(TreePlacement tree, Vector position, Func<IBlock, IBlock>? transform = null)
    {
        if (!this.ValidTreePos(tree.Tags, tree.Level, position))
        {
            return false;
        }

        var state = tree.TrunkState(position);
        var placed = transform is null ? state : transform(state);

        tree.SetLog(position, placed);
        return true;
    }

    protected void PlaceLogIfFree(TreePlacement tree, Vector position)
    {
        if (this.IsFree(tree.Tags, tree.Level, position))
        {
            this.PlaceLog(tree, position);
        }
    }

    /// <summary>Vanilla <c>trySetValue(AXIS, axis)</c>.</summary>
    protected static Func<IBlock, IBlock> WithAxis(string axis) => block => block.WithProperty("axis", axis);

    protected static string AxisOf(BlockFace face) => face switch
    {
        BlockFace.Up or BlockFace.Down => "y",
        BlockFace.North or BlockFace.South => "z",
        _ => "x"
    };

    protected static Vector Step(BlockFace face) => Vector.Zero.Offset(face);

    protected static int DistManhattan(Vector a, Vector b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);
}

internal sealed class StraightTrunkPlacer(JsonElement json) : TrunkPlacer(json)
{
    public override List<FoliageAttachment> PlaceTrunk(TreePlacement tree, int freeTreeHeight, Vector origin)
    {
        tree.PlaceBelowTrunk(origin + Vector.Down);

        for (var y = 0; y < freeTreeHeight; y++)
        {
            this.PlaceLog(tree, origin + (0, y, 0));
        }

        return [new FoliageAttachment(origin + (0, freeTreeHeight, 0), 0, false)];
    }
}

/// <summary>A 2x2 trunk; the three outer columns stop one block below the first.</summary>
internal class GiantTrunkPlacer(JsonElement json) : TrunkPlacer(json)
{
    public override List<FoliageAttachment> PlaceTrunk(TreePlacement tree, int freeTreeHeight, Vector origin)
    {
        var below = origin + Vector.Down;

        tree.PlaceBelowTrunk(below);
        tree.PlaceBelowTrunk(below + Vector.East);
        tree.PlaceBelowTrunk(below + Vector.South);
        tree.PlaceBelowTrunk(below + Vector.South + Vector.East);

        for (var y = 0; y < freeTreeHeight; y++)
        {
            this.PlaceLogIfFree(tree, origin + (0, y, 0));

            if (y < freeTreeHeight - 1)
            {
                this.PlaceLogIfFree(tree, origin + (1, y, 0));
                this.PlaceLogIfFree(tree, origin + (1, y, 1));
                this.PlaceLogIfFree(tree, origin + (0, y, 1));
            }
        }

        return [new FoliageAttachment(origin + (0, freeTreeHeight, 0), 0, true)];
    }
}

/// <summary>A giant trunk with short branches every few blocks down its upper half.</summary>
internal sealed class MegaJungleTrunkPlacer(JsonElement json) : GiantTrunkPlacer(json)
{
    public override List<FoliageAttachment> PlaceTrunk(TreePlacement tree, int freeTreeHeight, Vector origin)
    {
        var random = tree.Random;
        var attachments = base.PlaceTrunk(tree, freeTreeHeight, origin);
        var branchY = freeTreeHeight - 2 - random.NextInt(4);

        while (branchY > freeTreeHeight / 2)
        {
            var angle = random.NextFloat() * (float)(Math.PI * 2);
            var x = 0;
            var z = 0;

            for (var i = 0; i < 5; i++)
            {
                x = (int)(1.5f + Mth.Cos(angle) * i);
                z = (int)(1.5f + Mth.Sin(angle) * i);
                this.PlaceLog(tree, origin + (x, branchY - 3 + i / 2, z));
            }

            attachments.Add(new FoliageAttachment(origin + (x, branchY, z), -2, false));
            branchY -= 2 + random.NextInt(4);
        }

        return attachments;
    }
}

/// <summary>Acacia: a trunk leaning one way near the top, with an optional branch leaning another.</summary>
internal sealed class ForkingTrunkPlacer(JsonElement json) : TrunkPlacer(json)
{
    public override List<FoliageAttachment> PlaceTrunk(TreePlacement tree, int freeTreeHeight, Vector origin)
    {
        var random = tree.Random;
        tree.PlaceBelowTrunk(origin + Vector.Down);

        var attachments = new List<FoliageAttachment>();
        var leanDirection = FeatureHelpers.RandomHorizontal(random);
        var leanHeight = freeTreeHeight - random.NextInt(4) - 1;
        var leanSteps = 3 - random.NextInt(3);
        var x = origin.X;
        var z = origin.Z;
        int? topY = null;

        for (var i = 0; i < freeTreeHeight; i++)
        {
            var y = origin.Y + i;

            if (i >= leanHeight && leanSteps > 0)
            {
                var step = Step(leanDirection);
                x += step.X;
                z += step.Z;
                leanSteps--;
            }

            if (this.PlaceLog(tree, new Vector(x, y, z)))
            {
                topY = y + 1;
            }
        }

        if (topY is not null)
        {
            attachments.Add(new FoliageAttachment(new Vector(x, topY.Value, z), 1, false));
        }

        x = origin.X;
        z = origin.Z;
        var branchDirection = FeatureHelpers.RandomHorizontal(random);

        if (branchDirection == leanDirection)
        {
            return attachments;
        }

        var branchStart = leanHeight - random.NextInt(2) - 1;
        var branchLength = 1 + random.NextInt(3);
        topY = null;

        for (var i = branchStart; i < freeTreeHeight && branchLength > 0; branchLength--)
        {
            if (i >= 1)
            {
                var y = origin.Y + i;
                var step = Step(branchDirection);
                x += step.X;
                z += step.Z;

                if (this.PlaceLog(tree, new Vector(x, y, z)))
                {
                    topY = y + 1;
                }
            }

            i++;
        }

        if (topY is not null)
        {
            attachments.Add(new FoliageAttachment(new Vector(x, topY.Value, z), 0, false));
        }

        return attachments;
    }
}

/// <summary>Azalea: a trunk that bends sideways near its top, with foliage along the bend.</summary>
internal sealed class BendingTrunkPlacer(FeatureRegistry registry, JsonElement json) : TrunkPlacer(json)
{
    private readonly int minHeightForLeaves = json.TryGetProperty("min_height_for_leaves", out var min)
        ? min.GetInt32()
        : 1;

    private readonly IntProvider bendLength = registry.Int(json.GetProperty("bend_length"));

    public override List<FoliageAttachment> PlaceTrunk(TreePlacement tree, int freeTreeHeight, Vector origin)
    {
        var random = tree.Random;
        var direction = FeatureHelpers.RandomHorizontal(random);
        var step = Step(direction);
        var top = freeTreeHeight - 1;
        var cursor = origin;
        var attachments = new List<FoliageAttachment>();

        tree.PlaceBelowTrunk(cursor + Vector.Down);

        for (var i = 0; i <= top; i++)
        {
            if (i + 1 >= top + random.NextInt(2))
            {
                cursor += step;
            }

            if (tree.Tags.ValidTreePos(tree.Level, cursor))
            {
                this.PlaceLog(tree, cursor);
            }

            if (i >= this.minHeightForLeaves)
            {
                attachments.Add(new FoliageAttachment(cursor, 0, false));
            }

            cursor += Vector.Up;
        }

        var length = this.bendLength(random);

        for (var i = 0; i <= length; i++)
        {
            if (tree.Tags.ValidTreePos(tree.Level, cursor))
            {
                this.PlaceLog(tree, cursor);
            }

            attachments.Add(new FoliageAttachment(cursor, 0, false));
            cursor += step;
        }

        return attachments;
    }
}

/// <summary>A 2x2 trunk leaning near its top, with short hanging branches around its crown.</summary>
internal sealed class DarkOakTrunkPlacer(JsonElement json) : TrunkPlacer(json)
{
    public override List<FoliageAttachment> PlaceTrunk(TreePlacement tree, int freeTreeHeight, Vector origin)
    {
        var random = tree.Random;
        var attachments = new List<FoliageAttachment>();
        var below = origin + Vector.Down;

        tree.PlaceBelowTrunk(below);
        tree.PlaceBelowTrunk(below + Vector.East);
        tree.PlaceBelowTrunk(below + Vector.South);
        tree.PlaceBelowTrunk(below + Vector.South + Vector.East);

        var leanDirection = FeatureHelpers.RandomHorizontal(random);
        var leanHeight = freeTreeHeight - random.NextInt(4);
        var leanSteps = 2 - random.NextInt(3);
        var trunkX = origin.X;
        var trunkZ = origin.Z;
        var topY = origin.Y + freeTreeHeight - 1;

        for (var i = 0; i < freeTreeHeight; i++)
        {
            if (i >= leanHeight && leanSteps > 0)
            {
                var step = Step(leanDirection);
                trunkX += step.X;
                trunkZ += step.Z;
                leanSteps--;
            }

            var position = new Vector(trunkX, origin.Y + i, trunkZ);

            if (tree.Tags.IsAirOrLeaves(tree.Level, position))
            {
                this.PlaceLog(tree, position);
                this.PlaceLog(tree, position + Vector.East);
                this.PlaceLog(tree, position + Vector.South);
                this.PlaceLog(tree, position + Vector.East + Vector.South);
            }
        }

        attachments.Add(new FoliageAttachment(new Vector(trunkX, topY, trunkZ), 0, true));

        for (var dx = -1; dx <= 2; dx++)
        {
            for (var dz = -1; dz <= 2; dz++)
            {
                var outside = dx < 0 || dx > 1 || dz < 0 || dz > 1;

                if (!outside || random.NextInt(3) > 0)
                {
                    continue;
                }

                var length = random.NextInt(3) + 2;

                for (var i = 0; i < length; i++)
                {
                    this.PlaceLog(tree, new Vector(origin.X + dx, topY - i - 1, origin.Z + dz));
                }

                attachments.Add(new FoliageAttachment(new Vector(origin.X + dx, topY, origin.Z + dz), 0, false));
            }
        }

        return attachments;
    }
}

/// <summary>
/// 26.3's poplar: a straight trunk with up to four sideways logs at one height, where the single foliage attachment
/// sits. Every log redraws a shuffle of the six directions (vanilla's <c>Direction.allShuffled</c>), so the draws
/// match even where no branch grows.
/// </summary>
internal sealed class PoplarTrunkPlacer(FeatureRegistry registry, JsonElement json) : TrunkPlacer(json)
{
    private static readonly BlockFace[] directions =
    [
        BlockFace.Down,
        BlockFace.Up,
        BlockFace.North,
        BlockFace.South,
        BlockFace.West,
        BlockFace.East
    ];

    private readonly IntProvider trunkHeightAboveBranches =
        registry.Int(json.GetProperty("trunk_height_above_branches"));

    private readonly IntProvider branchAmount = registry.Int(json.GetProperty("branch_amount"));

    public override List<FoliageAttachment> PlaceTrunk(TreePlacement tree, int freeTreeHeight, Vector origin)
    {
        var random = tree.Random;
        tree.PlaceBelowTrunk(origin + Vector.Down);

        var branchHeight = freeTreeHeight - this.trunkHeightAboveBranches(random);

        for (var y = 0; y < freeTreeHeight; y++)
        {
            var position = origin + (0, y, 0);
            this.PlaceLog(tree, position);

            var branchDirections = ShuffledHorizontal(random);

            if (y != branchHeight - 1)
            {
                continue;
            }

            var branches = this.branchAmount(random);

            for (var i = 0; i < branches; i++)
            {
                var direction = branchDirections[i];
                this.PlaceLog(tree, position.Offset(direction), WithAxis(AxisOf(direction)));
            }
        }

        return [new FoliageAttachment(origin + (0, branchHeight, 0), 0, false)];
    }

    // Vanilla's Util.shuffle over all six directions, then the horizontal ones in their shuffled order.
    private static List<BlockFace> ShuffledHorizontal(IRandomSource random)
    {
        var shuffled = directions.ToArray();

        for (var i = shuffled.Length; i > 1; i--)
        {
            var j = random.NextInt(i);
            (shuffled[i - 1], shuffled[j]) = (shuffled[j], shuffled[i - 1]);
        }

        return shuffled.Where(face => !face.IsVertical()).ToList();
    }
}
