using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A one-block stump with a log lying on the ground next to it, like vanilla's FallenTreeFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:fallen_tree")]
public sealed class FallenTreeFeature : ConfiguredFeatureBase
{
    private static readonly BlockSet replaceableByTrees = new("#minecraft:replaceable_by_trees");

    public override string Type => "minecraft:fallen_tree";

    public required IBlockStateProvider TrunkProvider { get; init; }

    /// <summary>
    /// Total length including the stump and the gap; the lying log is 2 shorter.
    /// </summary>
    public required IIntProvider LogLength { get; init; }

    public required ImmutableArray<FallenTreeDecorator> StumpDecorators { get; init; }

    public required ImmutableArray<FallenTreeDecorator> LogDecorators { get; init; }

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var stump = this.PlaceLogBlock(level, random, origin, null);
        Decorate(level, random, [stump], this.StumpDecorators);

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
            Decorate(level, random, FeatureHelpers.JavaHashSetOrder(logs), this.LogDecorators);
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

    private static void Decorate(IWorldGenLevel level, IRandomSource random, List<Vector> logs, ImmutableArray<FallenTreeDecorator> decorators)
    {
        if (decorators.Length == 0)
            return;

        // TreeDecorator.Context sorts the logs by Y with a stable sort.
        var sorted = logs.OrderBy(log => log.Y).ToList();
        foreach (var decorator in decorators)
            decorator.Place(level, random, sorted);
    }

    private static bool ValidTreePos(IWorldGenLevel level, Vector position)
    {
        var state = level.GetBlock(position);
        return state.IsAir || replaceableByTrees.Contains(state);
    }

    private static bool IsOverSolidGround(IWorldGenLevel level, Vector position) =>
        level.GetBlock(position + Vector.Down).IsFaceSturdy(BlockFace.Up);
}

/// <summary>
/// A tree decorator applied to a fallen tree's stump or log, like vanilla's TreeDecorator with only logs in its context.
/// </summary>
/// <remarks>
/// Fallen trees only use <c>attached_to_logs</c> and <c>trunk_vine</c>, which are implemented here against the log list.
/// </remarks>
public abstract class FallenTreeDecorator
{
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Decorates the logs (sorted by Y like vanilla's decorator context).
    /// </summary>
    public abstract void Place(IWorldGenLevel level, IRandomSource random, IReadOnlyList<Vector> logs);
}

/// <summary>
/// Vanilla's AttachedToLogsDecorator: places blocks (mushrooms) next to random logs in one of <see cref="Directions"/>.
/// </summary>
[ConfiguredFeatureProperty("minecraft:attached_to_logs")]
public sealed class AttachedToLogsFallenTreeDecorator : FallenTreeDecorator
{
    public required float Probability { get; init; }

    public required IBlockStateProvider BlockProvider { get; init; }

    public required ImmutableArray<BlockFace> Directions { get; init; }

    public override void Place(IWorldGenLevel level, IRandomSource random, IReadOnlyList<Vector> logs)
    {
        foreach (var log in FeatureHelpers.ShuffledCopy(logs, random))
        {
            var direction = this.Directions[random.NextInt(this.Directions.Length)];
            var position = log.Offset(direction);
            if (random.NextFloat() <= this.Probability && level.GetBlock(position).IsAir)
                level.SetBlock(position, this.BlockProvider.GetState(random, position));
        }
    }
}

/// <summary>
/// Vanilla's TrunkVineDecorator: vines on the sides of each log with a 2 in 3 chance per side.
/// </summary>
[ConfiguredFeatureProperty("minecraft:trunk_vine")]
public sealed class TrunkVineFallenTreeDecorator : FallenTreeDecorator
{
    private static IBlock Vine => field ??= BlocksRegistry.Get(Material.Vine);

    public override void Place(IWorldGenLevel level, IRandomSource random, IReadOnlyList<Vector> logs)
    {
        foreach (var log in logs)
        {
            PlaceVine(level, random, log + Vector.West, "east");
            PlaceVine(level, random, log + Vector.East, "west");
            PlaceVine(level, random, log + Vector.North, "south");
            PlaceVine(level, random, log + Vector.South, "north");
        }
    }

    private static void PlaceVine(IWorldGenLevel level, IRandomSource random, Vector position, string face)
    {
        if (random.NextInt(3) > 0 && level.GetBlock(position).IsAir)
            level.SetBlock(position, Vine.WithProperty(face, true));
    }
}
