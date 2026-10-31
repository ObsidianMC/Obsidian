using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData;

internal sealed class SulfurSpikeBehavior : IShapeUpdateBehavior, IScheduledTickBehavior,
    IRandomTickBehavior, IBlockBehavior
{
    internal static bool BreaksFromTrident(double speed, bool allowed) => allowed && speed > 0.6;

    private static BlockFace Direction(IBlock state) =>
        state.GetProperty("vertical_direction") == "down" ? BlockFace.Down : BlockFace.Up;

    private static bool Points(IBlock state, BlockFace direction) =>
        TagsRegistry.Block.Speleothems.Entries.Contains(state.RegistryId) && Direction(state) == direction;

    internal static bool Survives(IBlockLevel level, Vector position, IBlock state)
    {
        var direction = Direction(state);
        var supportPosition = position.Offset(direction.Opposite());
        var support = level.GetBlock(supportPosition);
        return level.IsFaceSturdy(supportPosition, direction)
            || support.RegistryId == state.RegistryId && Points(support, direction);
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
        if (!state.Is(BlockIds.SulfurSpike) || !direction.IsVertical())
            return state;

        var tip = Direction(state);
        if (tip == BlockFace.Down && level.HasScheduledTick(position, state))
            return state;

        if (direction == tip.Opposite() && !Survives(level, position, state))
        {
            // Vanilla delays a falling ceiling spike by two ticks and a broken floor spike by one.
            level.ScheduleTick(position, state, tip == BlockFace.Down ? 2 : 1);
            return state;
        }

        return Shape(level, position, state, state.GetProperty("thickness") == "tip_merge");
    }

    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (!block.Is(BlockIds.SulfurSpike))
            return;

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
            var falling = level.Level.SpawnFallingBlock(cursor, state);
            if (falling is FallingBlock entity && state.GetProperty("thickness") is "tip" or "tip_merge")
            {
                entity.StalactiteDamage = Math.Max(1 + position.Y - cursor.Y, 6);
                break;
            }

            cursor += Vector.Down;
        }
    }

    public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random) =>
        GrowOnRandomTick(level.BlockUpdates, position, block, random);

    internal static void GrowOnRandomTick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        // Matches vanilla's growth chance, two-block sulfur search limit and subsequent boolean draw.
        if (random.NextFloat() >= 0.011377778f
            || !Points(block, BlockFace.Down)
            || level.GetBlock(position + Vector.Up).RegistryId != BlockIds.Sulfur)
        {
            return;
        }

        var tip = position;
        if (block.GetProperty("thickness") != "tip")
        {
            tip += Vector.Down;
        }

        var state = level.GetBlock(tip);
        if (state.RegistryId != block.RegistryId || !Points(state, BlockFace.Down)
            || state.GetProperty("thickness") != "tip" || state.GetProperty("waterlogged") == "true"
            || !CanGrow(level, tip, state))
        {
            return;
        }

        if (random.NextBoolean())
        {
            Extend(level, tip, state, BlockFace.Down);
            return;
        }

        var cursor = tip;
        for (var distance = 0; distance < 10; distance++)
        {
            cursor += Vector.Down;
            var below = level.GetBlock(cursor);
            if (!FluidState.Of(below).IsEmpty)
                return;

            if (below.RegistryId == block.RegistryId && Points(below, BlockFace.Up)
                && below.GetProperty("thickness") == "tip" && CanGrow(level, cursor, below))
            {
                Extend(level, cursor, below, BlockFace.Up);
                return;
            }

            var growing = block.WithProperty("vertical_direction", "up");
            if (Survives(level, cursor, growing) && !FluidState.Of(level.GetBlock(cursor + Vector.Down)).IsWater)
            {
                Extend(level, cursor + Vector.Down, growing, BlockFace.Up);
                return;
            }
        }
    }

    private static bool CanGrow(IBlockLevel level, Vector position, IBlock state)
    {
        var front = level.GetBlock(position.Offset(Direction(state)));
        return FluidState.Of(front).IsEmpty && (front.IsAir
            || front.RegistryId == state.RegistryId && Points(front, Direction(state).Opposite())
                && front.GetProperty("thickness") == "tip");
    }

    private static void Extend(IBlockLevel level, Vector position, IBlock state, BlockFace direction)
    {
        var target = position.Offset(direction);
        var existing = level.GetBlock(target);
        var growing = BlocksRegistry.GetDefault(state.RegistryId)
            .WithProperty("vertical_direction", direction == BlockFace.Down ? "down" : "up");
        if (existing.RegistryId == state.RegistryId && Points(existing, direction.Opposite())
            && existing.GetProperty("thickness") == "tip")
        {
            // Vanilla writes the downward tip before the upward tip when they join.
            var upper = direction == BlockFace.Down ? position : target;
            level.SetBlock(upper, growing.WithProperty("vertical_direction", "down")
                .WithProperty("thickness", "tip_merge"));
            level.SetBlock(upper + Vector.Down, growing.WithProperty("vertical_direction", "up")
                .WithProperty("thickness", "tip_merge"));
        }
        else if (existing.IsAir || existing.Is(BlockIds.Water))
        {
            level.SetBlock(target, growing.WithProperty("waterlogged", FluidState.Of(existing).IsWater));
        }
    }
}
