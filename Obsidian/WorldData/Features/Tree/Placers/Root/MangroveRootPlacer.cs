using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.Tree.Placers.Root;

/// <summary>
/// Mangrove roots: a column under the raised trunk plus four randomly winding roots, one per horizontal direction.
/// </summary>
[ConfiguredFeatureProperty("minecraft:mangrove_root_placer")]
public sealed class MangroveRootPlacer : RootPlacer
{
    public required MangroveRootPlacement MangroveRootPlacement { get; init; }

    public override bool PlaceRoots(TreeContext tree, Vector origin, Vector trunkOrigin)
    {
        var positions = new List<Vector>();
        for (var position = origin; position.Y < trunkOrigin.Y; position += Vector.Up)
        {
            if (!this.CanPlaceRoot(tree.Level, position))
                return false;
        }

        positions.Add(trunkOrigin + Vector.Down);
        foreach (var direction in TreeDirections.Horizontal)
        {
            var start = trunkOrigin + direction.ToVector();
            var root = new List<Vector>();
            if (!this.SimulateRoots(tree, start, direction, trunkOrigin, root, 0))
                return false;

            positions.AddRange(root);
            positions.Add(start);
        }

        foreach (var position in positions)
            this.PlaceRoot(tree, position);

        return true;
    }

    protected override bool CanPlaceRoot(IWorldGenLevel level, Vector position) =>
        base.CanPlaceRoot(level, position) || this.MangroveRootPlacement.CanGrowThrough.Contains(level.GetBlock(position));

    /// <summary>Roots replacing mud become muddy mangrove roots instead.</summary>
    protected override void PlaceRoot(TreeContext tree, Vector position)
    {
        if (this.MangroveRootPlacement.MuddyRootsIn.Contains(tree.Level.GetBlock(position)))
        {
            var block = this.MangroveRootPlacement.MuddyRootsProvider.GetState(tree.Random, position);
            tree.SetRoot(position, GetPotentiallyWaterloggedState(tree.Level, position, block));
        }
        else
        {
            base.PlaceRoot(tree, position);
        }
    }

    private bool SimulateRoots(TreeContext tree, Vector position, BlockFace direction, Vector trunkOrigin, List<Vector> root, int depth)
    {
        var maxLength = this.MangroveRootPlacement.MaxRootLength;
        if (depth == maxLength || root.Count > maxLength)
            return false;

        foreach (var next in this.PotentialRootPositions(position, direction, tree.Random, trunkOrigin))
        {
            if (this.CanPlaceRoot(tree.Level, next))
            {
                root.Add(next);
                if (!this.SimulateRoots(tree, next, direction, trunkOrigin, root, depth + 1))
                    return false;
            }
        }

        return true;
    }

    private Vector[] PotentialRootPositions(Vector position, BlockFace direction, IRandomSource random, Vector trunkOrigin)
    {
        var below = position + Vector.Down;
        var outward = position + direction.ToVector();
        var distance = TreeDirections.DistManhattan(position, trunkOrigin);
        var maxWidth = this.MangroveRootPlacement.MaxRootWidth;
        var skewChance = this.MangroveRootPlacement.RandomSkewChance;

        if (distance > maxWidth - 3 && distance <= maxWidth)
            return random.NextFloat() < skewChance ? [below, outward + Vector.Down] : [below];

        if (distance > maxWidth)
            return [below];

        if (random.NextFloat() < skewChance)
            return [below];

        return random.NextBoolean() ? [outward] : [below];
    }
}

/// <summary>
/// Settings of <see cref="MangroveRootPlacer"/>, like vanilla's <c>MangroveRootPlacement</c>.
/// </summary>
public sealed class MangroveRootPlacement
{
    public required BlockSet CanGrowThrough { get; init; }

    public required BlockSet MuddyRootsIn { get; init; }

    public required IBlockStateProvider MuddyRootsProvider { get; init; }

    public required int MaxRootWidth { get; init; }

    public required int MaxRootLength { get; init; }

    public required float RandomSkewChance { get; init; }
}
