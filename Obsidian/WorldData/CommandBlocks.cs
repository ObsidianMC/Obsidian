using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Commands;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.BlockTicks;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

/// <summary>
/// A command block's mode, which its block decides, in the order of vanilla's <c>CommandBlockEntity.Mode</c> (which
/// the edit packet sends).
/// </summary>
internal enum CommandBlockMode
{
    /// <summary>A chain command block: runs when the block behind it in a chain does.</summary>
    Sequence,

    /// <summary>A repeating command block: runs every tick while powered or always active.</summary>
    Auto,

    /// <summary>An impulse command block: runs once each time it's powered.</summary>
    Redstone
}

/// <summary>
/// The impulse, repeating and chain command blocks, like vanilla's <c>CommandBlock</c>: a rising redstone signal (or
/// being always active) schedules a tick a game tick later, whose run goes on down the chain of chain command blocks
/// the block faces. Their state is a <see cref="CommandBlockEntity"/>'s.
/// </summary>
/// <remarks>
/// A due tick runs on the level's entity path, where commands can run (see
/// <see cref="IRedstoneLevel.RunCommandBlock"/>), later in the same game tick than vanilla's, which runs it among the
/// block ticks.
/// </remarks>
internal static partial class CommandBlocks
{
    /// <summary>The command blocks' block ids.</summary>
    public static readonly int[] Ids =
        [BlockIds.CommandBlock, BlockIds.RepeatingCommandBlock, BlockIds.ChainCommandBlock];

    // Vanilla's scheduleTick delay for a command block.
    private const int TickDelay = 1;

    // Vanilla's max_command_sequence_length default.
    private const int DefaultSequenceLength = 65536;

    /// <summary>Whether <paramref name="block"/> is a command block of any mode.</summary>
    public static bool Is(IBlock? block) => block is not null && Array.IndexOf(Ids, block.RegistryId) >= 0;

    /// <summary>The mode <paramref name="block"/> runs in (vanilla's <c>getMode</c>).</summary>
    public static CommandBlockMode ModeOf(IBlock block) => block.RegistryId switch
    {
        BlockIds.RepeatingCommandBlock => CommandBlockMode.Auto,
        BlockIds.ChainCommandBlock => CommandBlockMode.Sequence,
        _ => CommandBlockMode.Redstone
    };

    /// <summary>The block of a mode, facing north and unconditional.</summary>
    public static IBlock BlockOf(CommandBlockMode mode) => mode switch
    {
        CommandBlockMode.Sequence => BlocksRegistry.ChainCommandBlock,
        CommandBlockMode.Auto => BlocksRegistry.RepeatingCommandBlock,
        _ => BlocksRegistry.CommandBlock
    };

    /// <summary>Whether a new block entity of <paramref name="block"/> is always active: a chain block's is.</summary>
    public static bool StartsAutomatic(IBlock block) => block.Is(BlockIds.ChainCommandBlock);

    /// <summary>Registers the command blocks' neighbour reaction and ticks.</summary>
    public static void Register(IRedstoneLevel level, LevelBlockTicks ticks, NeighborBehaviors neighbors,
        RedstoneEngine engine)
    {
        var reaction = new PowerReaction(level, engine);
        foreach (var id in Ids)
        {
            neighbors.Register(id, reaction);
            ticks.Register(id, (position, _) => level.RunCommandBlock(position));
        }
    }

    /// <summary>
    /// Vanilla's <c>setPoweredAndUpdate</c>: records whether redstone powers the block, and on a rising edge an
    /// impulse or repeating block that isn't always active checks its condition and schedules its tick.
    /// </summary>
    public static void SetPowered(IRedstoneLevel level, Vector position, IBlock state, CommandBlockEntity entity,
        bool powered)
    {
        if (entity.Powered == powered)
            return;

        entity.Powered = powered;
        entity.SetChanged();
        if (!powered || entity.Automatic || ModeOf(state) == CommandBlockMode.Sequence)
            return;

        MarkConditionMet(level, position, state, entity);
        level.ScheduleBlockTick(position, state, TickDelay);
    }

    /// <summary>
    /// Vanilla's <c>setAutomatic</c>: turning "Always Active" on for an unpowered impulse or repeating block checks
    /// its condition and schedules its tick.
    /// </summary>
    public static void SetAutomatic(IRedstoneLevel level, Vector position, IBlock state, CommandBlockEntity entity,
        bool automatic)
    {
        var was = entity.Automatic;
        entity.Automatic = automatic;
        entity.SetChanged();
        if (was || !automatic || entity.Powered || ModeOf(state) == CommandBlockMode.Sequence)
            return;

        ScheduleTick(level, position, state, entity);
    }

    /// <summary>
    /// Vanilla's <c>onModeSwitch</c>: a block that became a repeating one while powered or always active starts
    /// ticking.
    /// </summary>
    public static void OnModeSwitch(IRedstoneLevel level, Vector position, IBlock state, CommandBlockEntity entity)
    {
        if (ModeOf(state) == CommandBlockMode.Auto && (entity.Powered || entity.Automatic))
            ScheduleTick(level, position, state, entity);
    }

    /// <summary>
    /// Vanilla's <c>markConditionMet</c>: an unconditional block's condition holds; a conditional one's holds when
    /// the block behind it (against its facing) is a command block whose command last succeeded.
    /// </summary>
    public static bool MarkConditionMet(IRedstoneLevel level, Vector position, IBlock state,
        CommandBlockEntity entity)
    {
        var met = true;
        if (state.GetProperty("conditional") == "true")
        {
            var behind = position - Facing(state).ToVector();
            met = Is(level.GetLoadedBlock(behind))
                && level.GetBlockEntityAt(behind) is CommandBlockEntity previous
                && previous.Command.SuccessCount > 0;
        }

        if (entity.ConditionMet != met)
        {
            entity.ConditionMet = met;
            entity.SetChanged();
        }

        return met;
    }

    /// <summary>
    /// Vanilla's <c>CommandBlock.tick</c>, for a due tick of the block at <paramref name="position"/>: an impulse block
    /// runs if its condition held when it was powered; a repeating one runs if its condition held at its last check,
    /// checks it again for the next tick, and ticks again while powered or always active. A run goes on down the
    /// chain; a block that didn't run because its condition failed has its success count cleared. Comparators read
    /// the block again afterwards.
    /// </summary>
    /// <remarks>
    /// Runs on the level's entity path, holding no lock: the block's state is read, and its condition checked,
    /// under the level's tick lock, where edits and redstone change them, and the command runs outside it.
    /// </remarks>
    public static async ValueTask TickAsync(AbstractLevel level, Vector position)
    {
        var updates = level.BlockUpdates;
        IRedstoneLevel redstone = level;
        IBlock state;
        CommandBlockEntity entity;
        bool conditionMet;
        lock (level.Fluids.TickLock)
        {
            state = updates.GetBlock(position);
            if (!Is(state) || updates.GetBlockEntity(position) is not CommandBlockEntity found)
                return;

            entity = found;
            conditionMet = entity.ConditionMet;
            if (ModeOf(state) == CommandBlockMode.Auto)
                MarkConditionMet(redstone, position, state, entity);
        }

        var mode = ModeOf(state);
        if (mode != CommandBlockMode.Sequence)
            await RunOrClearAsync(level, position, state, entity, conditionMet);

        if (mode == CommandBlockMode.Auto && (entity.Powered || entity.Automatic))
            redstone.ScheduleBlockTick(position, state, TickDelay);

        level.NotifyComparatorsOf(position);
    }

    // The tick's run, or a conditional block's success count cleared when its condition failed.
    private static async ValueTask RunOrClearAsync(AbstractLevel level, Vector position, IBlock state,
        CommandBlockEntity entity, bool conditionMet)
    {
        if (conditionMet)
        {
            await ExecuteAsync(level, position, state, entity);
            return;
        }

        if (state.GetProperty("conditional") == "true")
            ClearSuccesses(entity);
    }

    // Vanilla's execute: the command runs (an empty one clears the success count), then the chain it faces.
    private static async ValueTask ExecuteAsync(AbstractLevel level, Vector position, IBlock state,
        CommandBlockEntity entity)
    {
        if (entity.Command.Command.Length > 0)
            await PerformAsync(level, position, state, entity);
        else
            ClearSuccesses(entity);

        await ExecuteChainAsync(level, position, Facing(state));
    }

    // Vanilla's executeChain: each chain command block in turn, the way the one before it faces, runs if it's powered
    // or always active and its condition holds, until a block that isn't a chain block, one that already ran this
    // tick, or the max_command_sequence_length rule's number of steps.
    private static async ValueTask ExecuteChainAsync(AbstractLevel level, Vector start, BlockFace direction)
    {
        var steps = level.LevelData.GetIntegerRule("max_command_sequence_length", DefaultSequenceLength);
        var position = start;
        var left = steps;
        while (left-- > 0)
        {
            position = position.Offset(direction);
            if (Link(level, position) is not var (state, entity, step))
                break;

            if (step == ChainStep.Run)
            {
                if (!await PerformAsync(level, position, state, entity))
                    break;

                level.NotifyComparatorsOf(position);
            }
            else if (step == ChainStep.Clear)
            {
                ClearSuccesses(entity);
            }

            direction = Facing(state);
        }

        if (left <= 0)
            Log.ChainTooLong(level.Logger, Math.Max(steps, 0));
    }

    // What a chain does at the position, read under the level's tick lock: nothing past something other than a chain
    // command block; a block that's neither powered nor always active is passed over; one whose condition holds runs;
    // a conditional one whose condition failed has its success count cleared.
    private static (IBlock State, CommandBlockEntity Entity, ChainStep Step)? Link(AbstractLevel level,
        Vector position)
    {
        var updates = level.BlockUpdates;
        lock (level.Fluids.TickLock)
        {
            var state = updates.GetBlock(position);
            if (!state.Is(BlockIds.ChainCommandBlock)
                || updates.GetBlockEntity(position) is not CommandBlockEntity entity)
            {
                return null;
            }

            if (!entity.Powered && !entity.Automatic)
                return (state, entity, ChainStep.Pass);

            if (MarkConditionMet(level, position, state, entity))
                return (state, entity, ChainStep.Run);

            var conditional = state.GetProperty("conditional") == "true";
            return (state, entity, conditional ? ChainStep.Clear : ChainStep.Pass);
        }
    }

    private enum ChainStep
    {
        Pass,
        Run,
        Clear
    }

    // Runs the block's command from its centre, turned the way it faces.
    private static async ValueTask<bool> PerformAsync(AbstractLevel level, Vector position, IBlock state,
        CommandBlockEntity entity)
    {
        var center = (VectorD)position + new VectorD(0.5, 0.5, 0.5);
        var run = new CommandRun(level, center, (YawOf(Facing(state)), 0), Executor: null);
        var ran = await entity.Command.PerformAsync(run);
        entity.SetChanged();
        return ran;
    }

    private static void ClearSuccesses(CommandBlockEntity entity)
    {
        entity.Command.SuccessCount = 0;
        entity.SetChanged();
    }

    // Vanilla's scheduleTick: the condition is checked now, for the tick a game tick later.
    private static void ScheduleTick(IRedstoneLevel level, Vector position, IBlock state, CommandBlockEntity entity)
    {
        MarkConditionMet(level, position, state, entity);
        level.ScheduleBlockTick(position, state, TickDelay);
    }

    /// <summary>
    /// Vanilla's <c>setPlacedBy</c>, after a player placed a command block: a block whose item has no block entity
    /// data tracks its output as the <c>send_command_feedback</c> rule says and is always active if it's a chain
    /// block; then it checks whether redstone powers it.
    /// </summary>
    public static void OnPlacedByPlayer(AbstractLevel level, Vector position, IBlock state, ItemStack? item)
    {
        if (!Is(state) || level.BlockUpdates.GetBlockEntity(position) is not CommandBlockEntity entity)
            return;

        IRedstoneLevel redstone = level;
        var hasData = item?.GetComponent<EntityDataComponent>(DataComponentType.BlockEntityData) is not null;
        if (!hasData)
        {
            entity.Command.TrackOutput = level.LevelData.GetBooleanRule("send_command_feedback");
            SetAutomatic(redstone, position, state, entity, StartsAutomatic(state));
        }

        var powered = level.RedstoneInstall.Engine.HasNeighborSignal(position);
        SetPowered(redstone, position, state, entity, powered);
    }

    /// <summary>The way the block faces.</summary>
    public static BlockFace Facing(IBlock state) => FeatureHelpers.ParseFace(state.GetProperty("facing"));

    // Vanilla's Direction.toYRot: south 0, west 90, north 180, east 270, and up and down 270 (their 2D value is -1,
    // masked to 3).
    private static float YawOf(BlockFace face) => face switch
    {
        BlockFace.South => 0,
        BlockFace.West => 90,
        BlockFace.North => 180,
        _ => 270
    };

    /// <summary>Vanilla's <c>neighborChanged</c>: the block records whether redstone powers it.</summary>
    private sealed class PowerReaction(IRedstoneLevel level, RedstoneEngine engine) : INeighborChangedBehavior
    {
        public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world)
        {
            if (level.GetBlockEntityAt(change.Position) is CommandBlockEntity entity)
            {
                var powered = engine.HasNeighborSignal(change.Position);
                SetPowered(level, change.Position, change.State, entity, powered);
            }

            return ValueTask.CompletedTask;
        }
    }

    private static partial class Log
    {
        [Microsoft.Extensions.Logging.LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning,
            Message = "Command Block chain tried to execute more than {Steps} steps!")]
        public static partial void ChainTooLong(Microsoft.Extensions.Logging.ILogger logger, int steps);
    }
}
