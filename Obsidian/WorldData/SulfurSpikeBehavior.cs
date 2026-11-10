using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>SpeleothemBlock</c>, which sulfur spikes and pointed dripstone share: placement, shape and support,
/// falling or breaking once the support has gone, and growth on random ticks. Pointed dripstone's own random tick drips
/// before it grows (see <see cref="DripstoneBehavior"/>).
/// </summary>
internal sealed class SulfurSpikeBehavior : IShapeUpdateBehavior, IScheduledTickBehavior,
    IRandomTickBehavior, IBlockBehavior
{
    // Vanilla's GROWTH_PROBABILITY_PER_RANDOM_TICK: the draw is part of the chunk's shared random sequence.
    private const float GrowthChance = 0.011377778f;

    // Vanilla's MAX_STALAGMITE_SEARCH_RANGE_WHEN_GROWING: how far below a stalactite's tip a stalagmite may start.
    private const int StalagmiteSearch = 10;

    // Sulfur spikes grow under sulfur, two long at most; pointed dripstone under a dripstone block with a water source
    // above it, seven long at most, and its stalagmites start only where a drip would reach (vanilla's canGrow,
    // getMaxGrowthLength and blocksStalagmiteScan).
    private static readonly GrowthRules sulfurGrowth =
        new(BlockIds.Sulfur, 2, NeedsWaterAbove: false, DripsReach: false);

    private static readonly GrowthRules dripstoneGrowth =
        new(BlockIds.DripstoneBlock, 7, NeedsWaterAbove: true, DripsReach: true);

    internal static bool BreaksFromTrident(double speed, bool allowed) => allowed && speed > 0.6;

    internal static BlockFace Direction(IBlock state) =>
        state.GetProperty("vertical_direction") == "down" ? BlockFace.Down : BlockFace.Up;

    /// <summary>Whether <paramref name="state"/> is a speleothem pointing <paramref name="direction"/>.</summary>
    internal static bool Points(IBlock state, BlockFace direction) =>
        TagsRegistry.Block.Speleothems.Entries.Contains(state.RegistryId) && Direction(state) == direction;

    internal static bool Survives(IBlockLevel level, Vector position, IBlock state) =>
        IsValidPlacement(level, position, Direction(state), state.RegistryId);

    /// <summary>
    /// Vanilla's <c>isTip</c>: a speleothem of any kind whose thickness is a tip, or a merged tip when
    /// <paramref name="includeMerged"/> holds.
    /// </summary>
    internal static bool IsTip(IBlock state, bool includeMerged)
    {
        if (!TagsRegistry.Block.Speleothems.Entries.Contains(state.RegistryId))
            return false;

        var thickness = state.GetProperty("thickness");
        return thickness == "tip" || includeMerged && thickness == "tip_merge";
    }

    /// <summary>Vanilla's <c>isFreeHangingStalactite</c>: an unmerged, dry stalactite tip.</summary>
    internal static bool IsFreeHangingStalactite(IBlock state) =>
        Points(state, BlockFace.Down)
        && state.GetProperty("thickness") == "tip"
        && state.GetProperty("waterlogged") != "true";

    /// <summary>Vanilla's <c>isStalactiteStartPos</c>: a stalactite with no more of its own block above it.</summary>
    internal static bool IsStalactiteStart(IBlockLevel level, Vector position, IBlock state) =>
        Points(state, BlockFace.Down) && level.GetBlock(position + Vector.Up).RegistryId != state.RegistryId;

    /// <summary>
    /// Vanilla's <c>findTip</c>: the tip of the column <paramref name="state"/> belongs to, searched along its
    /// direction through its own block for fewer than <paramref name="maxLength"/> steps, or null.
    /// </summary>
    internal static Vector? FindTip(IBlockLevel level, Vector position, IBlock state, int maxLength, bool includeMerged)
    {
        if (IsTip(state, includeMerged))
            return position;

        var direction = Direction(state);
        var cursor = position;
        for (var step = 1; step < maxLength; step++)
        {
            cursor = cursor.Offset(direction);
            var next = level.GetBlock(cursor);
            if (IsTip(next, includeMerged))
                return cursor;

            if (next.RegistryId != state.RegistryId || Direction(next) != direction)
                return null;
        }

        return null;
    }

    // Vanilla's isValidSpeleothemPlacement: the block behind holds the face up, or is the same block pointing the same
    // way.
    private static bool IsValidPlacement(IBlockLevel level, Vector position, BlockFace direction, int block)
    {
        var supportPosition = position.Offset(direction.Opposite());
        var support = level.GetBlock(supportPosition);
        return level.IsFaceSturdy(supportPosition, direction)
            || support.RegistryId == block && Points(support, direction);
    }

    private static IBlock Shape(IBlockLevel level, Vector position, IBlock state, bool merge)
    {
        var direction = Direction(state);
        var front = level.GetBlock(position.Offset(direction));
        var thickness = "tip";
        if (front.RegistryId == state.RegistryId && Points(front, direction.Opposite()))
        {
            if (merge || front.GetProperty("thickness") == "tip_merge")
            {
                thickness = "tip_merge";
            }
        }
        else if (Points(front, direction))
        {
            if (front.GetProperty("thickness") is "tip" or "tip_merge")
            {
                thickness = "frustum";
            }
            else
            {
                thickness = Points(level.GetBlock(position.Offset(direction.Opposite())), direction)
                    ? "middle" : "base";
            }
        }

        return state.WithProperty("thickness", thickness);
    }

    public IBlock? GetStateForPlacement(BlockPlaceContext context)
    {
        var direction = MathF.Sin(context.Player.Pitch.Degrees * (MathF.PI / 180)) >= 0
            ? BlockFace.Up : BlockFace.Down;
        var state = context.Block.WithProperty("vertical_direction", direction == BlockFace.Up ? "up" : "down");
        if (!Survives(context.Level, context.Position, state))
        {
            state = state.WithProperty("vertical_direction", direction == BlockFace.Up ? "down" : "up");
            if (!Survives(context.Level, context.Position, state))
                return null;
        }

        state = state.WithProperty("waterlogged", FluidState.Of(context.Level.GetBlock(context.Position)).IsWater);
        return Shape(context.Level, context.Position, state, !context.Player.Sneaking);
    }

    public IBlock? UpdateShape(
        IBlockLevel level,
        Vector position,
        IBlock state,
        BlockFace direction,
        Vector neighborPosition,
        IBlock neighbor)
    {
        if (!direction.IsVertical())
            return state;

        var tip = Direction(state);
        if (tip == BlockFace.Down && level.HasScheduledTick(position, state))
            return state;

        if (direction == tip.Opposite() && !Survives(level, position, state))
        {
            // Vanilla delays a falling stalactite by two ticks and a broken stalagmite by one.
            level.ScheduleTick(position, state, tip == BlockFace.Down ? 2 : 1);
            return state;
        }

        return Shape(level, position, state, state.GetProperty("thickness") == "tip_merge");
    }

    /// <summary>
    /// Vanilla's <c>SpeleothemBlock.tick</c>, scheduled when the support went: a stalagmite without support breaks,
    /// and a stalactite falls with everything hanging below it, down to and including its tip.
    /// </summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (Direction(block) == BlockFace.Up)
        {
            if (!Survives(level, position, block))
            {
                level.DestroyBlock(position, dropItems: true);
            }

            return;
        }

        var cursor = position;
        while (Points(level.GetBlock(cursor), BlockFace.Down))
        {
            var state = level.GetBlock(cursor);
            level.SetBlock(cursor, FluidState.Of(state).CreateLegacyBlock());

            // Vanilla's FallingBlockEntity.fall: the falling block leaves its water behind.
            var falling = level.Level.SpawnFallingBlock(cursor, state.WithProperty("waterlogged", false));
            if (falling is FallingBlock entity && IsTip(state, includeMerged: true))
            {
                entity.StalactiteDamage = Math.Max(1 + position.Y - cursor.Y, 6);
                break;
            }

            cursor += Vector.Down;
        }
    }

    public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random) =>
        GrowOnRandomTick(level.BlockUpdates, position, block, random);

    /// <summary>
    /// Vanilla's <c>SpeleothemBlock.randomTick</c>: a draw for the growth chance, and for a stalactite that can grow, a
    /// draw for whether its tip grows down or a stalagmite grows up below it.
    /// </summary>
    internal static void GrowOnRandomTick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        // Matches vanilla's draws and their order: the chance first, then (only for a tip that can grow) the side.
        if (random.NextFloat() >= GrowthChance || !IsStalactiteStart(level, position, block))
            return;

        var rules = block.Is(BlockIds.PointedDripstone) ? dripstoneGrowth : sulfurGrowth;
        if (!CanGrow(level, position, rules))
            return;

        if (FindTip(level, position, block, rules.MaxLength, includeMerged: false) is not Vector tip)
            return;

        var tipState = level.GetBlock(tip);
        if (!IsFreeHangingStalactite(tipState) || !CanTipGrow(level, tip, tipState, block))
            return;

        if (random.NextBoolean())
        {
            Grow(level, tip, BlockFace.Down, block);
            return;
        }

        GrowStalagmiteBelow(level, tip, block, rules);
    }

    // Vanilla's canGrow: the block the column hangs from, and for pointed dripstone a water source above that.
    private static bool CanGrow(IBlockLevel level, Vector position, GrowthRules rules)
    {
        if (level.GetBlock(position + Vector.Up).RegistryId != rules.GrowsOn)
            return false;

        return !rules.NeedsWaterAbove
            || FluidState.Of(level.GetBlock(position + new Vector(0, 2, 0))).Kind == FluidKind.Water;
    }

    // Vanilla's canTipGrow: the cell ahead of the tip holds no fluid, and is air or an unmerged tip of the same block
    // pointing back.
    private static bool CanTipGrow(IBlockLevel level, Vector tip, IBlock tipState, IBlock block)
    {
        var direction = Direction(tipState);
        var ahead = level.GetBlock(tip.Offset(direction));
        if (!FluidState.Of(ahead).IsEmpty)
            return false;

        return ahead.IsAir || IsUnmergedTip(ahead, direction.Opposite(), block);
    }

    private static bool IsUnmergedTip(IBlock state, BlockFace direction, IBlock block) =>
        IsTip(state, includeMerged: false) && Direction(state) == direction && state.RegistryId == block.RegistryId;

    // Vanilla's growStalagmiteBelow: the first cell below the tip where a stalagmite tip can grow up, or a stalagmite
    // can start on a floor that isn't water, within the search range; fluid or (for dripstone) a cell a drip can't
    // pass ends the search.
    private static void GrowStalagmiteBelow(IBlockLevel level, Vector tip, IBlock block, GrowthRules rules)
    {
        var cursor = tip;
        for (var distance = 0; distance < StalagmiteSearch; distance++)
        {
            cursor += Vector.Down;
            var state = level.GetBlock(cursor);
            if (!FluidState.Of(state).IsEmpty)
                return;

            if (IsUnmergedTip(state, BlockFace.Up, block) && CanTipGrow(level, cursor, state, block))
            {
                Grow(level, cursor, BlockFace.Up, block);
                return;
            }

            var floor = cursor + Vector.Down;
            if (IsValidPlacement(level, cursor, BlockFace.Up, block.RegistryId)
                && !FluidState.Of(level.GetBlock(floor)).IsWater)
            {
                Grow(level, floor, BlockFace.Up, block);
                return;
            }

            if (rules.DripsReach && !DripstoneBehavior.CanDripThrough(cursor, state))
                return;
        }
    }

    // Vanilla's grow: the cell ahead becomes a tip, or merges with an unmerged tip pointing back.
    private static void Grow(IBlockLevel level, Vector from, BlockFace direction, IBlock block)
    {
        var target = from.Offset(direction);
        var existing = level.GetBlock(target);
        if (IsUnmergedTip(existing, direction.Opposite(), block))
        {
            // Vanilla writes the downward tip before the upward tip when they join.
            var upper = Direction(existing) == BlockFace.Up ? target + Vector.Up : target;
            Create(level, upper, BlockFace.Down, "tip_merge", block);
            Create(level, upper + Vector.Down, BlockFace.Up, "tip_merge", block);
        }
        else if (existing.IsAir || existing.Is(BlockIds.Water))
        {
            Create(level, target, direction, "tip", block);
        }
    }

    // Vanilla's createSpeleothem: waterlogged when the cell held a water source.
    private static void Create(IBlockLevel level, Vector position, BlockFace direction, string thickness, IBlock block)
    {
        var waterlogged = FluidState.Of(level.GetBlock(position)).Kind == FluidKind.Water;
        var state = BlocksRegistry.GetDefault(block.RegistryId)
            .WithProperty("vertical_direction", direction == BlockFace.Down ? "down" : "up")
            .WithProperty("thickness", thickness)
            .WithProperty("waterlogged", waterlogged);
        level.SetBlock(position, state);
    }

    private sealed record GrowthRules(int GrowsOn, int MaxLength, bool NeedsWaterAbove, bool DripsReach);
}
