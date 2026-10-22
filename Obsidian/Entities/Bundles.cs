using Obsidian.API.Inventory;
using Obsidian.Events;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.WorldData;
using System.Collections.Immutable;

namespace Obsidian.Entities;

/// <summary>
/// Vanilla's <c>BundleItem</c> and <c>BundleContents</c>: what a bundle holds, how much room it has, and the click
/// overrides that move items in and out of it. Contents live in the stack's <c>minecraft:bundle_contents</c> component;
/// the selected index is kept on the stack and isn't saved, like vanilla's.
/// </summary>
internal static class Bundles
{
    private const int BundleWeightDenominator = 16;

    /// <summary>Vanilla's <c>BundleItem.overrideStackedOnOther</c> and <c>overrideOtherStackedOnMe</c>, for a click on <paramref name="slot"/>.</summary>
    /// <returns>Whether the click was handled and the usual pickup must not run.</returns>
    internal static bool TryClick(IPlayer player, BaseContainer container, int slot, bool primary)
    {
        var carried = player.CarriedItem;
        if (carried is not null && !carried.IsAir && ItemOwners.IsBundle(carried))
            return StackedOnOther(player, container, slot, carried, primary);

        var slotItem = container.GetItem(slot);
        if (slotItem is not null && !slotItem.IsAir && ItemOwners.IsBundle(slotItem))
            return OtherStackedOnMe(player, container, slot, slotItem, primary);

        return false;
    }

    /// <summary>Vanilla's <c>BundleItem.getUseDuration</c>: how long a player holds the bundle to empty it.</summary>
    internal const int UseDurationTicks = 200;

    /// <summary>
    /// Vanilla's <c>BundleItem.onUseTick</c> timing: contents drop on the first tick, then every second tick once
    /// fewer than 190 ticks remain.
    /// </summary>
    internal static bool DropsOnTick(int remainingTicks) =>
        remainingTicks == UseDurationTicks || remainingTicks < UseDurationTicks - 10 && remainingTicks % 2 == 0;

    /// <summary>The stack <see cref="RemoveOne"/> would take, without taking it.</summary>
    internal static ItemStack? Selected(ItemStack bundle)
    {
        var contents = Contents(bundle);
        if (contents.IsEmpty)
            return null;

        var index = bundle.BundleSelection >= 0 && bundle.BundleSelection < contents.Length ? bundle.BundleSelection : 0;
        return contents[index];
    }

    /// <summary>Vanilla's <c>BundleItem.toggleSelectedItem</c>, for the selection packet.</summary>
    internal static void ToggleSelection(ItemStack bundle, int index)
    {
        if (!ItemOwners.IsBundle(bundle))
            return;

        var contents = Contents(bundle);
        var valid = index >= 0 && index < contents.Length;
        bundle.BundleSelection = bundle.BundleSelection != index && valid ? index : -1;
    }

    /// <summary>Vanilla's <c>BundleContents.getMaxAmountToAdd</c>: how many of <paramref name="item"/> fit.</summary>
    internal static int Capacity(ItemStack bundle, ItemStack item)
    {
        var remaining = Weight.One.Subtract(TotalWeight(Contents(bundle)));
        if (remaining.Numerator <= 0)
            return 0;

        return (int)Math.Min(int.MaxValue, remaining.Divide(ItemWeight(item)));
    }

    /// <summary>
    /// Vanilla's <c>BundleContents.Mutable.tryInsert</c>: adds as many of <paramref name="item"/> as fit, merging into an
    /// equal stack and placing new stacks first.
    /// </summary>
    /// <returns>How many were inserted; the caller takes them from its own stack.</returns>
    internal static int Insert(ItemStack bundle, ItemStack item)
    {
        if (!ItemOwners.FitsInContainerItems(item))
            return 0;

        var count = Math.Min(item.Count, Capacity(bundle, item));
        if (count <= 0)
            return 0;

        var contents = Contents(bundle).ToList();
        var existingIndex = item.MaxStackSize > 1 ? contents.FindIndex(existing => SameKind(existing, item)) : -1;
        if (existingIndex >= 0)
        {
            var existing = contents[existingIndex];
            contents.RemoveAt(existingIndex);
            contents.Insert(0, new ItemStack(existing, existing.Count + count));
        }
        else
        {
            contents.Insert(0, new ItemStack(item, count));
        }

        Write(bundle, contents);
        return count;
    }

    /// <summary>
    /// Vanilla's <c>BundleContents.Mutable.removeOne</c>: removes the selected stack, or the first one when nothing is
    /// selected, and clears the selection.
    /// </summary>
    internal static ItemStack? RemoveOne(ItemStack bundle)
    {
        var contents = Contents(bundle).ToList();
        if (contents.Count == 0)
            return null;

        var index = bundle.BundleSelection >= 0 && bundle.BundleSelection < contents.Count ? bundle.BundleSelection : 0;
        var removed = contents[index];
        contents.RemoveAt(index);
        bundle.BundleSelection = -1;

        Write(bundle, contents);
        return removed;
    }

    /// <summary>Vanilla's <c>BundleContents.itemWeight</c>: the room one item of the stack takes up.</summary>
    internal static Weight ItemWeight(ItemStack item)
    {
        if (ItemOwners.IsBundle(item))
            return Weight.Of(1, BundleWeightDenominator).Add(TotalWeight(Contents(item)));

        if (item.GetComponent<BeesDataComponent>(DataComponentType.Bees) is { Occupants.Length: > 0 })
            return Weight.One;

        return Weight.Of(1, item.MaxStackSize);
    }

    internal static ImmutableArray<ItemStack> Contents(ItemStack bundle) =>
        bundle.GetComponent<SimpleDataComponent<ImmutableArray<ItemStack>>>(DataComponentType.BundleContents)?.Value ?? [];

    private static Weight TotalWeight(ImmutableArray<ItemStack> contents)
    {
        var total = Weight.Zero;
        foreach (var item in contents)
            total = total.Add(ItemWeight(item).Times(item.Count));

        return total;
    }

    private static bool SameKind(ItemStack left, ItemStack right) =>
        left.Holder == right.Holder && new ItemStack(left, 1).Equals(new ItemStack(right, 1));

    private static void Write(ItemStack bundle, List<ItemStack> contents) =>
        bundle[DataComponentType.BundleContents] = ComponentBuilder.BundleContents with { Value = [.. contents] };

    private static bool StackedOnOther(IPlayer player, BaseContainer container, int slot, ItemStack bundle, bool primary)
    {
        var level = player.Level as AbstractLevel;
        var position = (Vector)player.Position.Floor();
        var slotItem = container.GetItem(slot);
        var empty = slotItem is null || slotItem.IsAir;

        if (primary && !empty)
        {
            // Vanilla takes what fits from the slot and puts it in the bundle; the rest stays in the slot.
            var take = Math.Min(slotItem!.Count, Capacity(bundle, slotItem));
            var inserted = take > 0 && MainEventHandler.SlotMayPickup(player, container, slot)
                ? Insert(bundle, new ItemStack(slotItem, take))
                : 0;
            if (inserted > 0)
                container.RemoveItem(slot, inserted);

            Play(level, position, inserted > 0 ? "minecraft:item.bundle.insert" : "minecraft:item.bundle.insert_fail");
            return true;
        }

        if (!primary && empty)
        {
            // Vanilla takes one stack out of the bundle into the slot; what the slot doesn't take goes back.
            var removed = RemoveOne(bundle);
            if (removed is null)
                return true;

            var placed = MainEventHandler.SlotAccepts(player, container, slot, removed)
                ? Math.Min(removed.Count, MainEventHandler.SlotLimit(player, container, slot, removed))
                : 0;
            if (placed > 0)
                container.SetItem(slot, new ItemStack(removed, placed));

            if (placed < removed.Count)
                Insert(bundle, new ItemStack(removed, removed.Count - placed));

            Play(level, position, "minecraft:item.bundle.remove_one");
            return true;
        }

        return false;
    }

    private static bool OtherStackedOnMe(IPlayer player, BaseContainer container, int slot, ItemStack bundle, bool primary)
    {
        var level = player.Level as AbstractLevel;
        var position = (Vector)player.Position.Floor();
        var carried = player.CarriedItem;
        var cursorEmpty = carried is null || carried.IsAir;

        if (primary && !cursorEmpty)
        {
            // Vanilla puts the cursor's stack into the bundle, consuming what it takes.
            var mayPickup = MainEventHandler.SlotMayPickup(player, container, slot);
            var inserted = mayPickup ? Insert(bundle, carried!) : 0;
            if (inserted > 0)
            {
                carried!.Count -= inserted;
                if (carried.Count == 0)
                    player.CarriedItem = null;
            }

            Play(level, position, inserted > 0 ? "minecraft:item.bundle.insert" : "minecraft:item.bundle.insert_fail");
            return true;
        }

        if (!primary && cursorEmpty && MainEventHandler.SlotMayPickup(player, container, slot))
        {
            var removed = RemoveOne(bundle);
            if (removed is not null)
            {
                player.CarriedItem = removed;
                Play(level, position, "minecraft:item.bundle.remove_one");
            }

            return true;
        }

        bundle.BundleSelection = -1;
        return false;
    }

    private static void Play(AbstractLevel? level, Vector position, string sound)
    {
        level?.BlockUpdates.PlaySound(position, sound);
    }

    /// <summary>
    /// An exact fraction of a bundle's capacity, so that the floor of a capacity division is exact like vanilla's.
    /// </summary>
    internal readonly record struct Weight(long Numerator, long Denominator)
    {
        internal static readonly Weight Zero = new(0, 1);
        internal static readonly Weight One = new(1, 1);

        internal static Weight Of(long numerator, long denominator) => Reduce(numerator, denominator);

        internal Weight Add(Weight other) =>
            Reduce(this.Numerator * other.Denominator + other.Numerator * this.Denominator, this.Denominator * other.Denominator);

        internal Weight Subtract(Weight other) =>
            Reduce(this.Numerator * other.Denominator - other.Numerator * this.Denominator, this.Denominator * other.Denominator);

        internal Weight Times(long factor) => Reduce(this.Numerator * factor, this.Denominator);

        /// <summary>The whole number of <paramref name="divisor"/>s in this fraction (rounded down).</summary>
        internal long Divide(Weight divisor) =>
            this.Numerator * divisor.Denominator / (this.Denominator * divisor.Numerator);

        private static Weight Reduce(long numerator, long denominator)
        {
            if (denominator < 0)
            {
                numerator = -numerator;
                denominator = -denominator;
            }

            var gcd = GreatestCommonDivisor(Math.Abs(numerator), denominator);
            return gcd <= 1 ? new(numerator, denominator) : new(numerator / gcd, denominator / gcd);
        }

        private static long GreatestCommonDivisor(long a, long b)
        {
            while (b != 0)
                (a, b) = (b, a % b);

            return a == 0 ? 1 : a;
        }
    }
}
