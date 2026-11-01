using Obsidian.API.World.Features.Trees;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A one-block stump with a log lying on the ground next to it, like vanilla's FallenTreeFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:fallen_tree")]
public sealed class FallenTreeFeature : ConfiguredFeatureBase
{
    private static readonly BlockSet replaceableByTrees = WorldgenTags.Blocks("replaceable_by_trees").Selected;

    public override string Type => "minecraft:fallen_tree";

    public required IBlockStateProvider TrunkProvider { get; init; }

    /// <summary>
    /// Total length including the stump and the gap; the lying log is 2 shorter.
    /// </summary>
    public required IIntProvider LogLength { get; init; }

    /// <summary>
    /// Tree decorators run on the stump; like vanilla's, they see only logs (no leaves or roots).
    /// </summary>
    public required ImmutableArray<TreeDecorator> StumpDecorators { get; init; }

    /// <summary>
    /// Tree decorators run on the lying log; like vanilla's, they see only logs (no leaves or roots).
    /// </summary>
    public required ImmutableArray<TreeDecorator> LogDecorators { get; init; }

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var stump = this.PlaceLogBlock(level, random, origin, null);
        Decorate(context, [stump], this.StumpDecorators);

        var direction = FeatureHelpers.RandomHorizontal(random);
        var length = this.LogLength.Sample(random) - 2;
        var start = origin.Offset(direction, 2 + random.NextInt(2));
        start = FindGround(level, start);

        if (CanPlaceEntireFallenLog(level, length, start, direction))
        {
            var logs = new List<Vector>(length);
            var position = start;
            for (var i = 0; i < length; i++)
            {
                logs.Add(this.PlaceLogBlock(level, random, position, direction));
                position = position.Offset(direction);
            }

            // Vanilla collects the log positions in a HashSet before decorating.
            Decorate(context, FeatureHelpers.JavaHashSetOrder(logs), this.LogDecorators);
        }

        return true;
    }

    private static Vector FindGround(IWorldGenLevel level, Vector position)
    {
        position += Vector.Up;
        for (var i = 0; i < 6; i++)
        {
            if (ValidTreePos(level, position) && IsOverSolidGround(level, position))
                return position;

            position += Vector.Down;
        }

        return position;
    }

    private static bool CanPlaceEntireFallenLog(IWorldGenLevel level, int length, Vector position, BlockFace direction)
    {
        var gap = 0;
        for (var i = 0; i < length; i++)
        {
            if (!ValidTreePos(level, position))
                return false;

            if (!IsOverSolidGround(level, position))
            {
                if (++gap > 2)
                    return false;
            }
            else
            {
                gap = 0;
            }

            position = position.Offset(direction);
        }

        return true;
    }

    private Vector PlaceLogBlock(IWorldGenLevel level, IRandomSource random, Vector position, BlockFace? sideways)
    {
        var state = this.TrunkProvider.GetState(random, position);
        if (sideways is not null)
            state = state.WithProperty("axis", sideways.Value is BlockFace.East or BlockFace.West ? "x" : "z");

        level.SetBlock(position, state);
        return position;
    }

    private static void Decorate(FeatureContext context, List<Vector> logs, ImmutableArray<TreeDecorator> decorators)
    {
        if (decorators.Length == 0)
            return;

        // TreeDecorator.Context sorts the logs by Y with a stable sort.
        var view = new LogsView(context, [.. logs.OrderBy(log => log.Y)]);
        foreach (var decorator in decorators)
            decorator.Place(view);
    }

    private static bool ValidTreePos(IWorldGenLevel level, Vector position)
    {
        var state = level.GetBlock(position);
        return state.IsAir || replaceableByTrees.Contains(state);
    }

    private static bool IsOverSolidGround(IWorldGenLevel level, Vector position) =>
        level.GetBlock(position + Vector.Down).IsFaceSturdy(BlockFace.Up);

    // What the decorators see of a fallen tree: like vanilla's, a decorator context with only logs, whose blocks are
    // set straight into the level.
    private sealed class LogsView(FeatureContext context, Vector[] logs) : TreeDecoratorContext
    {
        public override IWorldGenLevel Level => context.Level;

        public override IRandomSource Random => context.Random;

        public override WorldGenerationContext Generation => context.Generation;

        public override IReadOnlyList<Vector> Logs => logs;

        public override IReadOnlyList<Vector> Leaves => [];

        public override IReadOnlyList<Vector> Roots => [];

        public override void SetBlock(Vector position, IBlock block) => context.Level.SetBlock(position, block);
    }
}
