using System.Collections.Frozen;
using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData;

/// <summary>
/// How live coral dies out of water, shared by vanilla's coral classes: a coral with no water touching it (and, for a
/// plant or fan, not waterlogged) schedules its death 60 to 99 ticks later, drawing the delay from the level's random,
/// and dies on that tick if it is still dry.
/// </summary>
internal static class Coral
{
    // Vanilla's 60 + random.nextInt(40): the draw is part of the level's random sequence.
    private const int DeathDelay = 60;

    private const int DeathDelaySpread = 40;

    private static readonly BlockFace[] faces =
        [BlockFace.Down, BlockFace.Up, BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];

    /// <summary>Vanilla's <c>scanForWater</c>: water (flowing, or a waterlogged block's) touches a face.</summary>
    public static bool TouchesWater(IShapeLevel level, Vector position)
    {
        foreach (var face in faces)
        {
            if (FluidState.Of(level.GetBlock(position.Offset(face))).IsWater)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Schedules <paramref name="state"/>'s death, drawing its delay from <paramref name="random"/>. Like vanilla, a
    /// neighbour's change draws the delay even while a death is already pending, and the scheduler drops the new tick.
    /// </summary>
    public static void ScheduleDeath(IShapeLevel level, Vector position, IBlock state, IRandomSource random) =>
        level.ScheduleTick(position, state, DeathDelay + random.NextInt(DeathDelaySpread));

    /// <summary>The dead coral of <paramref name="state"/>'s kind, in its default state.</summary>
    public static IBlock Dead(IBlock state) => deadForms.Value[state.RegistryId];

    // Each live coral's dead form, by registry id: vanilla names it dead_ followed by the live coral's name.
    private static readonly Lazy<FrozenDictionary<int, IBlock>> deadForms = new(() =>
    {
        string[] classes = ["CoralBlock", "CoralPlantBlock", "CoralFanBlock", "CoralWallFanBlock"];
        var forms = new Dictionary<int, IBlock>();
        foreach (var id in classes.SelectMany(BlockPhysics.BlocksOfClass))
        {
            var live = BlocksRegistry.GetDefault(id).UnlocalizedName;
            forms[id] = BlocksRegistry.Get(live.Replace("minecraft:", "minecraft:dead_"));
        }

        return forms.ToFrozenDictionary();
    });
}

/// <summary>Vanilla's <c>CoralBlock</c>: its <c>tick</c> and <c>updateShape</c> overrides.</summary>
internal sealed class CoralBlockBehavior : IScheduledTickBehavior, IShapeUpdateBehavior
{
    public IShapeUpdateBehavior? SharedPassRule => null;

    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (!Coral.TouchesWater(level.AsShapeLevel(), position))
            level.SetBlock(position, Coral.Dead(block), BlockUpdateFlags.Clients);
    }

    public IBlock? UpdateShape(
        IShapeLevel level,
        Vector position,
        IBlock state,
        BlockFace direction,
        Vector neighborPosition,
        IBlock neighbor)
    {
        if (!Coral.TouchesWater(level, position))
            Coral.ScheduleDeath(level, position, state, level.Random);

        return null;
    }
}

/// <summary>
/// Vanilla's <c>CoralPlantBlock</c>, <c>CoralFanBlock</c> and <c>CoralWallFanBlock</c>: their <c>onPlace</c>,
/// <c>tick</c> and <c>updateShape</c> overrides. A plant or fan stands on a sturdy floor; a wall fan hangs on the
/// sturdy face it faces away from, and its dead form keeps its facing.
/// </summary>
internal sealed class CoralPlantBehavior(bool onWall) : IOnPlaceBehavior, IScheduledTickBehavior, IShapeUpdateBehavior
{
    public IShapeUpdateBehavior? SharedPassRule => null;

    public void OnPlace(IBlockLevel level, Vector position, IBlock state, IBlock oldState)
    {
        var view = level.AsShapeLevel();
        if (!IsWet(view, position, state))
            Coral.ScheduleDeath(view, position, state, level.Random);
    }

    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (IsWet(level.AsShapeLevel(), position, block))
            return;

        var dead = Coral.Dead(block).WithProperty("waterlogged", false);
        if (onWall)
            dead = dead.WithProperty("facing", block.GetProperty("facing")!);

        level.SetBlock(position, dead, BlockUpdateFlags.Clients);
    }

    public IBlock? UpdateShape(
        IShapeLevel level,
        Vector position,
        IBlock state,
        BlockFace direction,
        Vector neighborPosition,
        IBlock neighbor)
    {
        // Losing its support breaks it before anything is drawn.
        var survives = Survives(level, position, state);
        if (direction == Support(state) && !survives)
            return BlocksRegistry.Air;

        // A coral without support is about to break: Obsidian's support check before the break asks every face's
        // shape update, which mustn't draw. Vanilla draws here only for a coral placed without support and without
        // updates, and changed beside it.
        if (survives && !IsWet(level, position, state))
            Coral.ScheduleDeath(level, position, state, level.Random);

        return null;
    }

    // Vanilla's BaseCoralPlantTypeBlock.scanForWater: waterlogged, or touching water.
    private static bool IsWet(IShapeLevel level, Vector position, IBlock state) =>
        state.GetProperty("waterlogged") == "true" || Coral.TouchesWater(level, position);

    // The face its support is on: below a plant or fan, behind a wall fan.
    private BlockFace Support(IBlock state) =>
        onWall ? FeatureHelpers.ParseFace(state.GetProperty("facing")!).Opposite() : BlockFace.Down;

    // Vanilla's canSurvive: the support's face toward the coral is sturdy.
    private bool Survives(IShapeLevel level, Vector position, IBlock state)
    {
        var support = Support(state);
        return level.IsFaceSturdy(position.Offset(support), support.Opposite());
    }
}
