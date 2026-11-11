using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

/// <summary>
/// What lightning does to the blocks it strikes: a lightning rod is powered for eight ticks (vanilla's
/// <c>LightningRodBlock.onLightningStrike</c>), and copper loses its weathering around the strike (vanilla's
/// <c>LightningBolt.clearCopperOnLightningStrike</c>).
/// </summary>
internal static class LightningRods
{
    // Vanilla's LightningRodBlock.ACTIVATION_TICKS.
    internal const int ActivationTicks = 8;

    private const string RodClass = "LightningRodBlock";
    private const string WeatheringRodClass = "WeatheringLightningRodBlock";

    /// <summary>Whether <paramref name="block"/> is a lightning rod, waxed, weathered or not.</summary>
    internal static bool IsRod(IBlock block)
    {
        var blockClass = block.BlockClass();
        return blockClass is RodClass or WeatheringRodClass;
    }

    /// <summary>
    /// Powers the lightning rod at <paramref name="position"/>, if there is one: the rod and the block it's attached to
    /// tell their neighbours, its power ends in eight ticks, and players see its sparks.
    /// </summary>
    internal static void Strike(AbstractLevel level, Vector position)
    {
        var blocks = level.BlockUpdates;
        var state = blocks.GetBlock(position);
        if (!IsRod(state))
            return;

        var powered = state.WithProperty("powered", "true");
        blocks.SetBlock(position, powered);
        UpdateAttached(blocks, position, state);
        blocks.ScheduleTick(position, powered, ActivationTicks);
        level.BroadcastLevelEvent(LightningBolt.SparkLevelEvent, position, Axis(Facing(state)));
    }

    /// <summary>
    /// Vanilla's <c>clearCopperOnLightningStrike</c>: struck weathering copper goes back to its first stage, and from
    /// struck copper (waxed or not) three to five random walks of one to eight steps each take a stage off the copper
    /// they step on, with sparks.
    /// </summary>
    /// <remarks>
    /// The draws come from the level's random in vanilla's order, which the level's later draws depend on.
    /// </remarks>
    internal static void ClearCopper(AbstractLevel level, Vector position)
    {
        var blocks = level.BlockUpdates;
        var struck = blocks.GetBlock(position);
        var weathering = CopperWeatheringBehavior.IsWeathering(struck);
        if (!weathering && !CopperWeatheringBehavior.IsWaxed(struck))
            return;

        if (weathering)
            blocks.SetBlock(position, CopperWeatheringBehavior.First(blocks.GetBlock(position)));

        var random = level.GrowthRandom;
        var walks = random.NextInt(3) + 3;
        for (var walk = 0; walk < walks; walk++)
        {
            var steps = random.NextInt(8) + 1;
            RandomWalk(level, position, steps, random);
        }
    }

    private static void RandomWalk(AbstractLevel level, Vector origin, int steps, IRandomSource random)
    {
        var at = origin;
        for (var step = 0; step < steps; step++)
        {
            if (RandomStep(level, at, random) is not Vector next)
                return;

            at = next;
        }
    }

    // Vanilla's randomStepCleaningCopper: up to ten random spots in the cube around the position, each drawn x, y then
    // z, and the first weathering copper among them loses a stage (if it has one to lose) and sparks.
    private static Vector? RandomStep(AbstractLevel level, Vector position, IRandomSource random)
    {
        var blocks = level.BlockUpdates;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var dx = random.NextInt(3) - 1;
            var dy = random.NextInt(3) - 1;
            var dz = random.NextInt(3) - 1;
            var candidate = position + new Vector(dx, dy, dz);
            var state = blocks.GetBlock(candidate);
            if (!CopperWeatheringBehavior.IsWeathering(state))
                continue;

            if (CopperWeatheringBehavior.Previous(state) is IBlock previous)
                blocks.SetBlock(candidate, previous);

            level.BroadcastLevelEvent(LightningBolt.SparkLevelEvent, candidate, -1);
            return candidate;
        }

        return null;
    }

    /// <summary>
    /// Registers the rods' power and what their going does, for both of vanilla's rod classes.
    /// </summary>
    internal static void Register(RedstoneBehaviors redstone, NeighborBehaviors neighbors)
    {
        foreach (var blockClass in (string[])[RodClass, WeatheringRodClass])
        {
            redstone.Register(blockClass, LightningRodBehavior.Instance);
            foreach (var id in BlockPhysics.BlocksOfClass(blockClass))
                neighbors.RegisterRemoved(id, LightningRodBehavior.Instance);
        }
    }

    /// <summary>
    /// The neighbours of the block a rod is attached to hear of its power changing, vanilla's <c>updateNeighbours</c>:
    /// through the level, so that redstone components react as well as plugin blocks.
    /// </summary>
    internal static void UpdateAttached(IBlockLevel blocks, Vector position, IBlock state)
    {
        var attached = position + Facing(state).Opposite().ToVector();
        blocks.Level.UpdateNeighbors(attached, state);
    }

    internal static BlockFace Facing(IBlock state) => FeatureHelpers.ParseFace(state.GetProperty("facing"));

    // Vanilla's Direction.Axis ordinal, which the sparks' level event carries: x, y, z.
    private static int Axis(BlockFace face) => face switch
    {
        BlockFace.East or BlockFace.West => 0,
        BlockFace.Up or BlockFace.Down => 1,
        _ => 2
    };
}

/// <summary>
/// Vanilla's <c>LightningRodBlock</c>: a powered rod gives power on every side, and strong power into the block it's
/// attached to; its power ends on its scheduled tick, and a powered rod placed by another block's change asks for one.
/// </summary>
internal sealed class LightningRodBehavior : IScheduledTickBehavior, IOnPlaceBehavior, IRedstoneBehavior, IRemovalBehavior
{
    internal static LightningRodBehavior Instance { get; } = new();

    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        level.SetBlock(position, block.WithProperty("powered", "false"));
        LightningRods.UpdateAttached(level, position, block);
    }

    public void OnPlace(IBlockLevel level, Vector position, IBlock state, IBlock oldState)
    {
        if (state.RegistryId == oldState.RegistryId || !Powered(state))
            return;

        if (!level.HasScheduledTick(position, state))
            level.ScheduleTick(position, state, LightningRods.ActivationTicks);
    }

    public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        Powered(state) ? RedstoneEngine.MaxSignal : 0;

    // Direct power is asked for in the direction from the receiver to the rod, which is the rod's facing for the block
    // it's attached to.
    public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        Powered(state) && LightningRods.Facing(state) == direction ? RedstoneEngine.MaxSignal : 0;

    // Vanilla's affectNeighborsAfterRemoval: a powered rod going tells the block it was attached to.
    public async ValueTask OnRemovedAsync(BlockChange change, IBlockChangeWorld world)
    {
        if (!Powered(change.Old))
            return;

        var attached = change.Position + LightningRods.Facing(change.Old).Opposite().ToVector();
        await world.NotifyNeighborsAsync(attached);
    }

    private static bool Powered(IBlock state) => state.GetProperty("powered") == "true";
}
