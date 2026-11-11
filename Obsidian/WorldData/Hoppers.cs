using Microsoft.Extensions.Logging;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

/// <summary>
/// How hoppers move items, like vanilla's <c>HopperBlockEntity</c>: on its block entity tick, a hopper off cooldown
/// and not powered pushes one item into the container it faces, then pulls one from the container above (or takes in
/// the item entities lying on it), and waits <see cref="HopperBlockEntity.MoveItemSpeed"/> ticks after either moved
/// something. Items go through item access (see <see cref="IItemAccess"/>), so they reach plugins' blocks too.
/// </summary>
/// <remarks>
/// <para>
/// Matches vanilla where contraptions depend on it: the cooldown's length and when it's set, slots tried in order
/// (the hopper's own from the first, the other container's in the order its side reaches them), one item a
/// transfer, and an empty hopper receiving items holding its next transfer back (a tick less when a hopper that
/// ticked no later in the same game tick pushed them, so that a chain of hoppers moves items at the same pace
/// whichever ticks first).
/// </para>
/// <para>
/// Runs from the block-entity tick and inside contact. Inventory admission finishes outside an item's short
/// transfer section; inherited tick or transfer locks allow only a nonblocking inventory attempt.
/// </para>
/// </remarks>
internal static class Hoppers
{
    // Vanilla's Hopper.SUCK_AABB: from the hopper's bowl (11 pixels up) to the top of the block above.
    private const double SuckBottom = 11 / 16.0;
    private const double SuckTop = 2;

    /// <summary>
    /// Vanilla <c>pushItemsTick</c>.
    /// </summary>
    public static async ValueTask TickAsync(AbstractLevel level, HopperBlockEntity hopper)
    {
        hopper.Cooldown--;
        hopper.TickedGameTime = level.LevelData.GameTime;

        if (hopper.Cooldown > 0)
            return;

        hopper.Cooldown = 0;

        await MoveItemsAsync(level, hopper);
    }

    /// <summary>Inside contact tries the same push/pull transaction, without advancing the tick cooldown.</summary>
    internal static ValueTask TouchItemAsync(AbstractLevel level, HopperBlockEntity hopper, ItemEntity item)
    {
        var position = hopper.BlockPosition;
        var min = new VectorD(position.X, position.Y + SuckBottom, position.Z);
        var max = new VectorD(position.X + 1, position.Y + SuckTop, position.Z + 1);
        return item.Removed || !Overlaps(item.BoundingBox, min, max)
            ? default : MoveItemsAsync(level, hopper, item);
    }

    private static async ValueTask MoveItemsAsync(AbstractLevel level, HopperBlockEntity hopper, ItemEntity? item = null)
    {
        if (hopper.Cooldown > 0 || hopper.IsRemoved)
            return;

        var state = level.BlockUpdates.GetBlock(hopper.BlockPosition);
        if (state.GetProperty("enabled") != "true" || hopper.GetItemAccess(side: null) is not IItemAccess own)
            return;

        var changed = false;
        if (!IsEmpty(own))
            changed = Eject(level, hopper, own, FeatureHelpers.ParseFace(state.GetProperty("facing")));

        if (!IsFull(own))
        {
            if (item is null)
            {
                changed |= await SuckInAsync(level, hopper, own);
            }
            else
            {
                var position = hopper.BlockPosition;
                var min = new VectorD(position.X, position.Y + SuckBottom, position.Z);
                var max = new VectorD(position.X + 1, position.Y + SuckTop, position.Z + 1);
                changed |= await TakeInAsync(own, item, level, min, max, position);
            }
        }

        if (changed)
        {
            hopper.Cooldown = HopperBlockEntity.MoveItemSpeed;
            hopper.SetChanged();
        }
    }

    /// <summary>
    /// Moves one item from <paramref name="slot"/> of <paramref name="source"/> into <paramref name="target"/>'s
    /// slots, in their order, when it fits: simulated on both ends first, then done.
    /// </summary>
    /// <param name="leftover">
    /// What neither end took back when a click changed one of them between the simulation and the move: the caller
    /// drops it, so that no item is lost. Null almost always.
    /// </param>
    /// <returns>Whether the item moved.</returns>
    public static bool TryMoveOne(IItemAccess source, int slot, IItemAccess target, out ItemStack? leftover)
    {
        leftover = null;

        if (source.Extract(slot, 1, simulate: true) is not ItemStack planned
            || target.Insert(planned, simulate: true) is not null
            || source.Extract(slot, 1) is not ItemStack taken)
        {
            return false;
        }

        if (target.Insert(taken) is not ItemStack refused)
            return true;

        leftover = source.Insert(slot, refused);
        return false;
    }

    /// <summary>
    /// Drops what <see cref="TryMoveOne"/> couldn't put anywhere, at the centre of <paramref name="position"/>.
    /// </summary>
    /// <summary>
    /// Drops items a container couldn't take at <paramref name="position"/>, in the block's middle. Where the level
    /// can't take the drop in (a chunk not loaded or unloading), it's kept in the chunk instead, or in the one at
    /// <paramref name="keepAt"/>, waiting to spawn and saved with the chunk until it does, so it's never lost.
    /// </summary>
    public static void DropLeftover(AbstractLevel level, Vector position, ItemStack? leftover, VectorD? keepAt = null)
    {
        if (leftover is null)
            return;

        var item = new ItemEntity
        {
            Level = level,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)position + new VectorD(0.5, 0.5, 0.5),
            Item = leftover
        };

        bool spawned;
        using (level.TransferLock.Enter())
        {
            spawned = level.AdmitsTravellersAt(item.Position) && level.TryAddEntity(item, hostOwned: true);
            if (!spawned && KeepInChunk(level, item))
                return;

            if (!spawned && keepAt is VectorD fallback)
            {
                item.Position = fallback;
                if (KeepInChunk(level, item))
                    return;
            }
        }

        if (spawned)
        {
            item.SpawnEntity();
            return;
        }

        // Nowhere loaded to keep it: tried again with the level's next tick.
        level.Logger.LogError("Items a container couldn't take could not be dropped or kept; retrying");
        level.EnqueueEntityAction(() =>
        {
            DropLeftover(level, position, leftover, keepAt);
            return ValueTask.CompletedTask;
        });
    }

    // Keeps a drop the level couldn't take in as an entity waiting to spawn in its chunk. Call holding the level's
    // transfer lock.
    private static bool KeepInChunk(AbstractLevel level, ItemEntity item) =>
        level.GetRegionForLocation(item.Position) is Region region && region.TryKeepUnplaced(item, level);

    // Vanilla's ejectItems: the first of the hopper's items that fits into the container it faces moves.
    private static bool Eject(AbstractLevel level, HopperBlockEntity hopper, IItemAccess own, BlockFace facing)
    {
        var targetPosition = hopper.BlockPosition.Offset(facing);
        if (level.GetItemAccess(targetPosition, facing.Opposite()) is not IItemAccess target)
            return false;

        // Vanilla's tryMoveInItem: a hopper this one fills from empty waits a tick less when it ticked no earlier.
        var targetHopper = level.BlockUpdates.GetBlockEntity(targetPosition) as HopperBlockEntity;
        var fromEmpty = targetHopper is { OnCustomCooldown: false } && IsEmpty(targetHopper.Inventory);

        for (var slot = 0; slot < own.SlotCount; slot++)
        {
            var moved = TryMoveOne(own, slot, target, out var leftover);
            DropLeftover(level, hopper.BlockPosition, leftover);

            if (!moved)
                continue;

            if (fromEmpty && targetHopper!.TickedGameTime >= hopper.TickedGameTime)
                targetHopper.Cooldown = HopperBlockEntity.MoveItemSpeed - 1;

            return true;
        }

        return false;
    }

    // Vanilla's suckInItems: one item from the first slot of the container above that gives one up; or, with no
    // container above and nothing solid there, the item entities in the hopper's bowl and the block above.
    private static async ValueTask<bool> SuckInAsync(AbstractLevel level, HopperBlockEntity hopper, IItemAccess own)
    {
        var above = hopper.BlockPosition + Vector.Up;
        if (level.GetItemAccess(above, BlockFace.Down) is IItemAccess source)
        {
            for (var slot = 0; slot < source.SlotCount; slot++)
            {
                var moved = TryMoveOne(source, slot, own, out var leftover);
                DropLeftover(level, above, leftover);

                if (moved)
                    return true;
            }

            return false;
        }

        var aboveState = level.BlockUpdates.GetBlock(above);
        if (aboveState.IsCollisionShapeFullBlock()
            && !TagsRegistry.Block.DoesNotBlockHoppers.Entries.Contains(aboveState.RegistryId))
        {
            return false;
        }

        var position = hopper.BlockPosition;
        var min = new VectorD(position.X, position.Y + SuckBottom, position.Z);
        var max = new VectorD(position.X + 1, position.Y + SuckTop, position.Z + 1);
        var items = level.BlockUpdates
            .EntitiesIn(min, max)
            .OfType<ItemEntity>()
            .OrderBy(item => item.EntityId)
            .ToList();

        foreach (var item in items)
        {
            if (await TakeInAsync(own, item, level, min, max, position))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Vanilla's <c>addItem(Container, ItemEntity)</c>: as much of the entity's stack as fits goes in; only taking
    /// all of it counts as a transfer.
    /// </summary>
    /// <param name="own">The slots taking the items.</param>
    /// <param name="entity">An item the hopper found in its area.</param>
    /// <param name="level">The hopper's level.</param>
    /// <param name="min">The area's low corner, which the item's box must still overlap.</param>
    /// <param name="max">The area's high corner.</param>
    /// <param name="dropAt">
    /// Where in <paramref name="level"/> what no longer fits is dropped: the hopper's own block, as the item may have
    /// moved on.
    /// </param>
    /// <remarks>
    /// The item may have moved, gone through a portal or been taken since it was found, so it's checked again under
    /// the transfer lock of the level owning it, and taken only while it's still in <paramref name="level"/> and the
    /// area, and is one of its entities (a removed or unloaded item keeps its level). The slots are filled outside that
    /// lock: filling them takes the container's click lock, which comes before
    /// transfer locks in the lock order. What fits is worked out first; anything that no longer fits by then, the
    /// slots having changed meanwhile, is dropped as a new item rather than lost.
    /// </remarks>
    internal static async ValueTask<bool> TakeInAsync(
        IItemAccess own, ItemEntity entity, AbstractLevel level, VectorD min, VectorD max, Vector dropAt)
    {
        if (entity.Item is not { IsAir: false, Count: > 0 } offered)
            return false;

        var probe = new ItemStack(offered, offered.Count);
        var fits = probe.Count - (own.Insert(probe, simulate: true)?.Count ?? 0);
        if (fits <= 0)
            return false;

        ItemStack taken;
        bool all;
        VectorD keepAt;
        using (entity.EnterOwningLevel(out var owner))
        {
            if (owner != level || entity.Removed || !entity.IsMemberOf(level) || !Overlaps(entity.BoundingBox, min, max))
                return false;

            if (entity.Item is not { IsAir: false, Count: > 0 } stack || stack != offered)
                return false;

            // Where the item is now, in this level: a loaded chunk to keep what no longer fits in, if need be.
            keepAt = entity.Position;
            var amount = Math.Min(fits, stack.Count);
            all = amount == stack.Count;
            taken = new ItemStack(stack, amount);
            entity.Item = all ? ItemStack.Air : new ItemStack(stack, stack.Count - amount);
            entity.Removed = all;
        }

        DropLeftover(level, dropAt, own.Insert(taken), keepAt);

        if (all)
            await entity.RemoveAsync();
        else
            entity.SendItemUpdate();

        return all;
    }

    // Whether a box overlaps the one from min to max, as the level's entity lookup tells (see EntitiesIn).
    private static bool Overlaps(BoundingBox box, VectorD min, VectorD max) =>
        box.Min.X < max.X && box.Max.X > min.X
        && box.Min.Y < max.Y && box.Max.Y > min.Y
        && box.Min.Z < max.Z && box.Max.Z > min.Z;

    internal static bool IsEmpty(IItemAccess access)
    {
        for (var slot = 0; slot < access.SlotCount; slot++)
        {
            if (access.GetItem(slot) is not null)
                return false;
        }

        return true;
    }

    private static bool IsEmpty(BaseContainer inventory) =>
        inventory.All(stack => stack is null || stack.IsAir || stack.Count <= 0);

    // Vanilla's inventoryFull: every slot holds a full stack.
    internal static bool IsFull(IItemAccess access)
    {
        for (var slot = 0; slot < access.SlotCount; slot++)
        {
            if (access.GetItem(slot) is not ItemStack stack || stack.Count != stack.MaxStackSize)
                return false;
        }

        return true;
    }
}
