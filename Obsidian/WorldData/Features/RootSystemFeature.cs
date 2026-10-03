using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Grows a tree (the azalea tree) on the surface above a cave and fills the column below with rooted dirt and hanging roots,
/// like vanilla's RootSystemFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:root_system")]
public sealed class RootSystemFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:root_system";

    /// <summary>
    /// The tree placed on top.
    /// </summary>
    public required PlacedFeature Feature { get; init; }

    public required int RequiredVerticalSpaceForTree { get; init; }

    public required int RootRadius { get; init; }

    public required BlockSet RootReplaceable { get; init; }

    public required IBlockStateProvider RootStateProvider { get; init; }

    public required int RootPlacementAttempts { get; init; }

    public required int RootColumnMaxHeight { get; init; }

    public required int HangingRootRadius { get; init; }

    public required int HangingRootsVerticalSpan { get; init; }

    public required IBlockStateProvider HangingRootStateProvider { get; init; }

    public required int HangingRootPlacementAttempts { get; init; }

    public required int AllowedVerticalWaterForTree { get; init; }

    public required IBlockPredicate AllowedTreePosition { get; init; }

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin) || !level.GetBlock(origin).IsAir)
            return false;

        if (this.PlaceDirtAndTree(context, origin))
            this.PlaceRoots(level, context.Random, origin);

        return true;
    }

    private bool PlaceDirtAndTree(FeatureContext context, Vector origin)
    {
        var level = context.Level;
        var position = origin;

        for (var i = 0; i < this.RootColumnMaxHeight; i++)
        {
            position += Vector.Up;
            if (!this.AllowedTreePosition.Test(level, position) || !this.HasSpaceForTree(level, position))
                continue;

            var below = level.GetBlock(position + Vector.Down);
            if (FeatureHelpers.IsLavaFluid(below) || !below.IsSolid())
                return false;

            if (this.Feature.Place(level, context.Generation, context.Random, position))
            {
                this.PlaceDirt(level, context.Random, origin, origin.Y + i);
                return true;
            }
        }

        return false;
    }

    private bool HasSpaceForTree(IWorldGenLevel level, Vector position)
    {
        for (var i = 1; i <= this.RequiredVerticalSpaceForTree; i++)
        {
            var state = level.GetBlock(position + new Vector(0, i, 0));
            if (!state.IsAir && !(i + 1 <= this.AllowedVerticalWaterForTree && FeatureHelpers.IsWaterFluid(state)))
                return false;
        }

        return true;
    }

    private void PlaceDirt(IWorldGenLevel level, IRandomSource random, Vector origin, int topY)
    {
        for (var y = origin.Y; y < topY; y++)
            this.PlaceRootedDirt(level, random, origin.X, origin.Z, y);
    }

    private void PlaceRootedDirt(IWorldGenLevel level, IRandomSource random, int x, int z, int y)
    {
        var radius = this.RootRadius;
        var position = new Vector(x, y, z);

        for (var i = 0; i < this.RootPlacementAttempts; i++)
        {
            // Offsets are applied to the current position, which is reset to the column after each attempt.
            position += new Vector(random.NextInt(radius) - random.NextInt(radius), 0, random.NextInt(radius) - random.NextInt(radius));
            if (this.RootReplaceable.Contains(level.GetBlock(position)))
                level.SetBlock(position, this.RootStateProvider.GetState(random, position));

            position = new Vector(x, position.Y, z);
        }
    }

    private void PlaceRoots(IWorldGenLevel level, IRandomSource random, Vector origin)
    {
        var radius = this.HangingRootRadius;
        var span = this.HangingRootsVerticalSpan;

        for (var i = 0; i < this.HangingRootPlacementAttempts; i++)
        {
            var position = origin + new Vector(random.NextInt(radius) - random.NextInt(radius), random.NextInt(span) - random.NextInt(span),
                random.NextInt(radius) - random.NextInt(radius));
            if (!level.GetBlock(position).IsAir)
                continue;

            var state = this.HangingRootStateProvider.GetState(random, position);
            if (state.CanSurvive(level, position) && level.GetBlock(position + Vector.Up).IsFaceSturdy(BlockFace.Down))
                level.SetBlock(position, state);
        }
    }
}
