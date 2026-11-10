using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData;

/// <summary>
/// What pointed dripstone does beyond the speleothem behaviour it shares with sulfur spikes
/// (<see cref="SulfurSpikeBehavior"/>): the water or lava above a stalactite drips from its tip, filling a cauldron
/// below or drying mud above into clay (vanilla's <c>PointedDripstoneBlock</c>), and the cauldron takes the drip on its
/// scheduled tick (vanilla's <c>AbstractCauldronBlock.tick</c>).
/// </summary>
internal static class DripstoneBehavior
{
    // Vanilla's WATER_TRANSFER_PROBABILITY_PER_RANDOM_TICK and LAVA_TRANSFER_PROBABILITY_PER_RANDOM_TICK, compared
    // with the random tick's first draw.
    private const float WaterDripChance = 0.17578125f;

    private const float LavaDripChance = 0.05859375f;

    // Vanilla's MAX_SEARCH_LENGTH_WHEN_CHECKING_DRIP_TYPE and MAX_SEARCH_LENGTH_BETWEEN_STALACTITE_TIP_AND_CAULDRON:
    // searches take fewer steps than this.
    private const int SearchLength = 11;

    // Vanilla's DRIPSTONE_DRIP level event, sent at the tip.
    private const int DripEvent = 1504;

    // Vanilla's DRIPSTONE_FILL_CAULDRON and DRIPSTONE_FILL_CAULDRON_LAVA level events.
    private const int WaterFillEvent = 1047;

    private const int LavaFillEvent = 1046;

    // Vanilla's REQUIRED_SPACE_TO_DRIP_THROUGH_NON_SOLID_BLOCK: a drip falls down the middle four pixels of a block.
    private const double DripMin = 6 / 16.0;

    private const double DripMax = 10 / 16.0;

    /// <summary>
    /// Vanilla's <c>PointedDripstoneBlock.maybeTransferFluid</c>, given the random tick's first draw: the top of a
    /// stalactite under water or lava, within reach of its tip, sometimes drips. Water above mud dries the mud into
    /// clay; otherwise the drip schedules the first cauldron below that can take it, within reach, to receive it.
    /// </summary>
    public static void MaybeDrip(AbstractLevel level, Vector position, IBlock block, float chance)
    {
        if (chance > WaterDripChance)
            return;

        var blocks = level.BlockUpdates;
        if (!SulfurSpikeBehavior.IsStalactiteStart(blocks, position, block))
            return;

        if (FluidAbove(level, position, block) is not { } source)
            return;

        var fluid = source.Fluid;
        var dripChance = fluid switch
        {
            FluidKind.Water => WaterDripChance,
            FluidKind.Lava => LavaDripChance,
            _ => 0f
        };
        if (chance >= dripChance)
            return;

        if (SulfurSpikeBehavior.FindTip(blocks, position, block, SearchLength, includeMerged: false) is not Vector tip)
            return;

        if (source.State.Is(BlockIds.Mud) && fluid == FluidKind.Water)
        {
            // Not ported: vanilla's Block.pushEntitiesUp, which lifts entities standing on the mud onto the clay.
            var clay = BlocksRegistry.GetDefault(BlockIds.Clay);
            blocks.SetBlock(source.Position, clay);
            level.EmitGameEvent(MobGameEvent.BlockChange, (VectorD)source.Position + 0.5, affectedBlock: clay);
            level.BroadcastLevelEvent(DripEvent, tip, 0);
            return;
        }

        if (FillableCauldronBelow(blocks, tip, fluid) is not Vector cauldron)
            return;

        level.BroadcastLevelEvent(DripEvent, tip, 0);

        // Vanilla's delay: 50 ticks, and a tick for each block the drip falls.
        var delay = 50 + tip.Y - cauldron.Y;
        blocks.ScheduleTick(cauldron, blocks.GetBlock(cauldron), delay);
    }

    /// <summary>
    /// Vanilla's <c>AbstractCauldronBlock.tick</c>: a cauldron under a free-hanging stalactite whose column has water
    /// or lava above it takes a drip of it, when it can.
    /// </summary>
    public static void ReceiveDrip(IBlockLevel blocks, Vector position, IBlock cauldron)
    {
        if (blocks.Level is not AbstractLevel level)
            return;

        if (StalactiteTipAbove(blocks, position) is not Vector tip)
            return;

        if (FluidAbove(level, tip, blocks.GetBlock(tip)) is not { } source)
            return;

        var fluid = source.Fluid;
        if (fluid is not (FluidKind.Water or FluidKind.Lava) || !CanReceive(cauldron, fluid))
            return;

        IBlock filled;
        int fillEvent;
        if (cauldron.Is(BlockIds.WaterCauldron))
        {
            var fill = int.Parse(cauldron.GetProperty("level")!);
            if (fill >= 3)
                return;

            filled = cauldron.WithProperty("level", fill + 1);
            fillEvent = WaterFillEvent;
        }
        else if (fluid == FluidKind.Water)
        {
            filled = BlocksRegistry.GetDefault(BlockIds.WaterCauldron);
            fillEvent = WaterFillEvent;
        }
        else
        {
            filled = BlocksRegistry.GetDefault(BlockIds.LavaCauldron);
            fillEvent = LavaFillEvent;
        }

        blocks.SetBlock(position, filled);
        level.EmitGameEvent(MobGameEvent.BlockChange, (VectorD)position + 0.5, affectedBlock: filled);
        level.BroadcastLevelEvent(fillEvent, position, 0);
    }

    /// <summary>
    /// Vanilla's <c>PointedDripstoneBlock.canDripThrough</c>: a drip passes air, and any block that isn't solid,
    /// holds no fluid, and has no collision down the middle four pixels of the cell.
    /// </summary>
    public static bool CanDripThrough(Vector position, IBlock state)
    {
        if (state.IsAir)
            return true;

        if (state.IsSolidRender() || !FluidState.Of(state).IsEmpty)
            return false;

        var stateId = state.GetHashCode();
        var (offsetX, _, offsetZ) = BlockPhysics.ShapeOffset(stateId, position.X, position.Z);
        foreach (var box in BlockPhysics.ShapeBoxes(stateId))
        {
            var overlaps = box[0] + offsetX < DripMax && box[3] + offsetX > DripMin
                && box[2] + offsetZ < DripMax && box[5] + offsetZ > DripMin
                && box[4] > box[1];
            if (overlaps)
                return false;
        }

        return true;
    }

    // Vanilla's getFluidAboveStalactite: the fluid in the cell above the block the stalactite's column hangs from,
    // where water stands for mud unless water evaporates there. Null for a column that ends out of reach.
    private static (Vector Position, FluidKind Fluid, IBlock State)? FluidAbove(
        AbstractLevel level,
        Vector stalactite,
        IBlock state)
    {
        if (!SulfurSpikeBehavior.Points(state, BlockFace.Down))
            return null;

        var blocks = level.BlockUpdates;
        var cursor = stalactite;
        for (var step = 1; step < SearchLength; step++)
        {
            cursor += Vector.Up;
            var next = blocks.GetBlock(cursor);
            if (next.RegistryId != state.RegistryId)
            {
                var above = cursor + Vector.Up;
                var aboveState = blocks.GetBlock(above);
                var fluid = aboveState.Is(BlockIds.Mud) && !level.Gameplay(LevelEnvironment.WaterEvaporates, above)
                    ? FluidKind.Water
                    : FluidState.Of(aboveState).Kind;
                return (above, fluid, aboveState);
            }

            if (SulfurSpikeBehavior.Direction(next) != BlockFace.Down)
                return null;
        }

        return null;
    }

    // Vanilla's findFillableCauldronBelowStalactiteTip: the first cell below the tip, within reach, that is a cauldron
    // able to take the fluid, with only cells a drip passes on the way.
    private static Vector? FillableCauldronBelow(IBlockLevel blocks, Vector tip, FluidKind fluid)
    {
        var cursor = tip;
        for (var step = 1; step < SearchLength; step++)
        {
            cursor += Vector.Down;
            var state = blocks.GetBlock(cursor);
            if (CanReceive(state, fluid))
                return cursor;

            if (!CanDripThrough(cursor, state))
                return null;
        }

        return null;
    }

    // Vanilla's findStalactiteTipAboveCauldron: the first free-hanging stalactite above, within reach, with only cells
    // a drip passes on the way.
    private static Vector? StalactiteTipAbove(IBlockLevel blocks, Vector cauldron)
    {
        var cursor = cauldron;
        for (var step = 1; step < SearchLength; step++)
        {
            cursor += Vector.Up;
            var state = blocks.GetBlock(cursor);
            if (SulfurSpikeBehavior.IsFreeHangingStalactite(state))
                return cursor;

            if (!CanDripThrough(cursor, state))
                return null;
        }

        return null;
    }

    // Vanilla's canReceiveStalactiteDrip: an empty cauldron takes either fluid, a water cauldron (rain's) water, and
    // lava and powder snow cauldrons nothing.
    private static bool CanReceive(IBlock state, FluidKind fluid) => state.RegistryId switch
    {
        BlockIds.Cauldron => fluid is FluidKind.Water or FluidKind.Lava,
        BlockIds.WaterCauldron => fluid == FluidKind.Water,
        _ => false
    };
}

/// <summary>Vanilla's <c>PointedDripstoneBlock.randomTick</c>: a draw for dripping, then the growth.</summary>
internal sealed class PointedDripstoneRandomTick : IRandomTickBehavior
{
    public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        // Vanilla's order: the drip's draw, then the growth's (SpeleothemBlock.randomTick).
        var dripChance = random.NextFloat();
        DripstoneBehavior.MaybeDrip(level, position, block, dripChance);
        SulfurSpikeBehavior.GrowOnRandomTick(level.BlockUpdates, position, block, random);
    }
}

/// <summary>Vanilla's <c>AbstractCauldronBlock.tick</c>, which only takes a stalactite's drip.</summary>
internal sealed class CauldronDripTick : IScheduledTickBehavior
{
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random) =>
        DripstoneBehavior.ReceiveDrip(level, position, block);
}

internal static partial class RandomTickBehaviors
{
    internal static IRandomTickBehavior PointedDripstoneBlock { get; } = new PointedDripstoneRandomTick();
}
