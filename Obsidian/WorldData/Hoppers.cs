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
/// Runs on the level's tick, without its tick lock: each step takes one container's lock at a time, and an item
/// entity's transfer lock before the hopper's.
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
        hopper.TickedGameTime = level.LevelData.Time;

        if (hopper.Cooldown > 0)
            return;

        hopper.Cooldown = 0;

        // Vanilla's tryMoveItems.
        var state = level.BlockUpdates.GetBlock(hopper.BlockPosition);
        if (state.GetProperty("enabled") != "true" || hopper.GetItemAccess(side: null) is not IItemAccess own)
            return;

        var changed = false;
        if (!IsEmpty(own))
            changed = Eject(level, hopper, own, FeatureHelpers.ParseFace(state.GetProperty("facing")));

        if (!IsFull(own))
            changed |= await SuckInAsync(level, hopper, own);

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
    public static void DropLeftover(AbstractLevel level, Vector position, ItemStack? leftover)
    {
        if (leftover is null)
            return;

        level.TrySpawnEntity(new ItemEntity
        {
            Level = level,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)position + new VectorD(0.5, 0.5, 0.5),
            Item = leftover
        }, hostOwned: true);
    }

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
        var items = level.BlockUpdates
            .EntitiesIn(
                new VectorD(position.X, position.Y + SuckBottom, position.Z),
                new VectorD(position.X + 1, position.Y + SuckTop, position.Z + 1))
            .OfType<ItemEntity>()
            .OrderBy(item => item.EntityId)
            .ToList();

        foreach (var item in items)
        {
            if (await TakeInAsync(own, item))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Vanilla's <c>addItem(Container, ItemEntity)</c>: as much of the entity's stack as fits goes in; only taking
    /// all of it counts as a transfer.
    /// </summary>
    internal static async ValueTask<bool> TakeInAsync(IItemAccess own, ItemEntity entity)
    {
        bool all;
        lock (ItemEntity.TransferLock)
        {
            if (entity.Removed || entity.Item is not { IsAir: false, Count: > 0 } stack)
                return false;

            var left = own.Insert(stack);
            if (ReferenceEquals(left, stack))
                return false;

            all = left is null;
            entity.Item = left ?? ItemStack.Air;
            entity.Removed = all;
        }

        if (all)
            await entity.RemoveAsync();
        else
            entity.SendItemUpdate();

        return all;
    }

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
