using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>randomTick</c> overrides of blocks that melt: ice (and frosted ice, whose class inherits it) and snow
/// layers. Members are named for the Mojang declaring classes in <c>block_random_tick_classes.json</c>.
/// </summary>
internal static partial class RandomTickBehaviors
{
    internal static IRandomTickBehavior IceBlock { get; } = new IceMelting();

    internal static IRandomTickBehavior SnowLayerBlock { get; } = new SnowLayerMelting();

    /// <summary>
    /// Vanilla's <c>IceBlock.randomTick</c>: ice melts in more block light than 11 less its light dampening. Sky light
    /// doesn't melt it. No draws.
    /// </summary>
    private sealed class IceMelting : IRandomTickBehavior
    {
        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            if (Melting.BlockLight(level, position) > 11 - block.LightBlock())
                Melting.Melt(level.BlockUpdates, position, block);
        }
    }

    /// <summary>
    /// Vanilla's <c>SnowLayerBlock.randomTick</c>: snow melts away in more block light than 11. No draws.
    /// </summary>
    /// <remarks>
    /// Vanilla drops the snow's loot first, but its table only drops for an entity, which a random tick has none of,
    /// so nothing drops and nothing is drawn.
    /// </remarks>
    private sealed class SnowLayerMelting : IRandomTickBehavior
    {
        public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
        {
            if (Melting.BlockLight(level, position) <= 11)
                return;

            level.BlockUpdates.RemoveBlock(position);
            level.EmitGameEvent(MobGameEvent.BlockDestroy, (VectorD)position + 0.5, affectedBlock: block);
        }
    }
}

/// <summary>
/// Vanilla's <c>FrostedIceBlock</c> overrides of <c>tick</c> and <c>onPlace</c>: frosted ice ages in light and melts at
/// its last age, sooner when few frosted ice blocks surround it.
/// </summary>
/// <remarks>
/// Vanilla's <c>neighborChanged</c> override, which melts frosted ice at once when a frosted ice neighbour goes and
/// leaves it fewer than two, isn't implemented: Obsidian has no neighbour-changed hook for vanilla blocks yet.
/// </remarks>
internal sealed class FrostedIceBlockBehavior : IScheduledTickBehavior, IOnPlaceBehavior
{
    // Vanilla's FrostedIceBlock.MAX_AGE and NEIGHBORS_TO_AGE.
    private const int MaxAge = 3;
    private const int NeighborsToAge = 4;

    // Vanilla's Direction.values() order, which decides the order of the neighbours' draws.
    private static readonly Vector[] Neighbours =
        [Vector.Down, Vector.Up, Vector.North, Vector.South, Vector.West, Vector.East];

    /// <summary>
    /// Vanilla's <c>FrostedIceBlock.onPlace</c>: a tick 60 to 120 game ticks later, drawn from the level random. It
    /// runs for every state change, so ageing draws it too.
    /// </summary>
    public void OnPlace(IBlockLevel level, Vector position, IBlock state, IBlock oldState) =>
        level.ScheduleTick(position, state, Between(level.Random, 60, 120));

    /// <summary>
    /// Vanilla's <c>FrostedIceBlock.tick</c>: on a draw of <c>nextInt(3)</c> that comes up 0, or with fewer than four
    /// frosted ice neighbours, frosted ice in enough light ages, or melts at its last age and ages its neighbours. Any
    /// other outcome schedules the next tick 20 to 40 game ticks later.
    /// </summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (level.Level is not AbstractLevel concrete)
            return;

        if (random.NextInt(3) == 0 || FewerNeighboursThan(level, position, NeighborsToAge))
        {
            var brightness = Brightness(concrete, position);
            var needed = 11 - Age(block) - block.LightBlock();
            if (brightness > needed && SlightlyMelt(level, position, block))
            {
                foreach (var direction in Neighbours)
                {
                    var neighbourPosition = position + direction;
                    var neighbour = level.GetBlock(neighbourPosition);
                    if (neighbour.Is(BlockIds.FrostedIce) && !SlightlyMelt(level, neighbourPosition, neighbour))
                        level.ScheduleTick(neighbourPosition, neighbour, Between(random, 20, 40));
                }

                return;
            }
        }

        level.ScheduleTick(position, block, Between(random, 20, 40));
    }

    // Vanilla's FrostedIceBlock.tick brightness: the block light in the end, elsewhere the raw brightness with the
    // sky darkened (getMaxLocalRawBrightness).
    private static int Brightness(AbstractLevel level, Vector position)
    {
        if (level.DimensionName == "minecraft:the_end")
            return Melting.BlockLight(level, position);

        return Growth.RawBrightness(level, position, level.SkyDarken());
    }

    // Vanilla's slightlyMelt: one age older (shown to clients, without neighbour updates), or melted at the last age.
    private static bool SlightlyMelt(IBlockLevel level, Vector position, IBlock block)
    {
        var age = Age(block);
        if (age < MaxAge)
        {
            level.SetBlock(position, block.WithProperty("age", age + 1), BlockUpdateFlags.Clients);
            return false;
        }

        Melting.Melt(level, position, block);
        return true;
    }

    private static bool FewerNeighboursThan(IBlockLevel level, Vector position, int limit)
    {
        var count = 0;
        foreach (var direction in Neighbours)
        {
            if (level.GetBlock(position + direction).Is(BlockIds.FrostedIce) && ++count >= limit)
                return false;
        }

        return true;
    }

    private static int Age(IBlock block) => int.Parse(block.GetProperty("age")!);

    // Vanilla's Mth.nextInt: one draw of nextInt(max - min + 1), offset by min.
    private static int Between(IRandomSource random, int min, int max) => random.NextInt(max - min + 1) + min;
}

/// <summary>
/// Vanilla's <c>IceBlock.melt</c>, which ice and frosted ice share.
/// </summary>
internal static class Melting
{
    /// <summary>
    /// Melts ice at <paramref name="position"/>: to nothing where water evaporates, as in the nether, and elsewhere to
    /// a water source that starts to flow (the level's write schedules its tick).
    /// </summary>
    public static void Melt(IBlockLevel level, Vector position, IBlock ice)
    {
        if (level.Level.Gameplay(LevelEnvironment.WaterEvaporates, position))
        {
            level.RemoveBlock(position);
            return;
        }

        level.SetBlock(position, BlocksRegistry.Water);
        if (level.Level is AbstractLevel concrete)
            concrete.EmitGameEvent(MobGameEvent.BlockDestroy, (VectorD)position + 0.5, affectedBlock: ice);
    }

    /// <summary>The block light at <paramref name="position"/>: 0 outside the build range or the loaded chunks.</summary>
    public static int BlockLight(AbstractLevel level, Vector position)
    {
        if (level.IsOutsideBuildHeight(position.Y))
            return 0;

        if (level.GetLoadedChunk(position.X >> 4, position.Z >> 4) is not IChunk chunk)
            return 0;

        return chunk.GetLightLevel(position.X, position.Y, position.Z, LightType.Block);
    }
}
