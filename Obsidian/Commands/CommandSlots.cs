using Obsidian.API.BlockEntities;
using Obsidian.API.Commands;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities;
using System.Text.Json;

namespace Obsidian.Commands;

/// <summary>Slot sources shared by /item and /execute. Slot ids come from the generated vanilla ranges.</summary>
internal static class CommandSlots
{
    /// <summary>One selected slot. <see cref="Set"/> reports whether the slot accepted the stack.</summary>
    internal sealed record Slot(object Owner, Func<ItemStack?> Get, Func<ItemStack?, bool> Set, Func<ValueTask> Sync,
        bool Visible = true, int Limit = int.MaxValue)
    {
        /// <summary>Set on the extra slot after a growable component's entries, which appends new entries.</summary>
        public GrowableContents? Growth { get; init; }
    }

    // Vanilla's ChargedProjectiles.Mutable stops adding entries at this many.
    private const int MaxChargedProjectiles = 1024;

    internal static int CountVisible(IEnumerable<Slot> slots) => slots.Count(slot => slot.Visible);

    internal static async Task<List<Slot>> ReadAsync(CommandContext context, CommandInput input)
    {
        var kind = input.Read();
        var sources = new List<Func<int, Slot?>>();
        if (kind == "entity")
        {
            if (!EntitySelector.TryParse(input.Read(), out var selector))
                throw new FormatException("Invalid entity selector");

            var entities = selector.Select(context);
            if (entities.Count == 0)
                throw new FormatException("No entity was found");

            sources.AddRange(entities.Select<IEntity, Func<int, Slot?>>(entity => id => EntitySlot(entity, id)));
        }
        else if (kind == "block")
        {
            if (!CommandCoordinates.TryParse(input.Position(), context, false, true, out var at))
                throw new FormatException("Invalid container position");

            var blockEntity = await CommandFeedback.LevelOf(context).GetBlockEntityAsync((Vector)at);
            if (blockEntity is not ContainerBlockEntity container)
                throw new FormatException("The block is not a container");

            sources.Add(id => InventorySlot(container.Inventory, id,
                () => Obsidian.Events.MainEventHandler.RefreshViewersAsync(container.Inventory)));
        }
        else
        {
            throw new FormatException("Expected entity or block");
        }

        var expression = input.Read();
        var result = new List<Slot>();
        foreach (var source in sources)
            result.AddRange(Resolve(expression, source));

        return result;
    }

    private static List<Slot> Resolve(string expression, Func<int, Slot?> source)
    {
        if (VanillaCommandTree.Reference.GetProperty("slot_ranges").TryGetProperty(expression, out var slots))
            return Range(slots, source);

        return Resolve(CommandInput.Data(expression), source);
    }

    private static List<Slot> Resolve(JsonElement expression, Func<int, Slot?> source)
    {
        if (expression.ValueKind == JsonValueKind.Array)
            return expression.EnumerateArray().SelectMany(term => Resolve(term, source)).ToList();

        if (expression.ValueKind == JsonValueKind.String)
        {
            var name = expression.GetString()!;
            if (VanillaCommandTree.Reference.GetProperty("slot_ranges").TryGetProperty(name, out var range))
                return Range(range, source);

            throw new FormatException($"Unknown slot source {name}");
        }

        var type = CommandInput.Qualify(expression.GetProperty("type").GetString()!);
        switch (type)
        {
            case "minecraft:empty":
                return [];

            case "minecraft:group":
                return Resolve(expression.GetProperty("terms"), source);

            case "minecraft:slot_range":
                if (expression.TryGetProperty("source", out var owner) && owner.GetString() != "container")
                    throw new FormatException("This slot context has only a container source");

                return Resolve(expression.GetProperty("slots"), source);

            case "minecraft:limit_slots":
                var limit = expression.GetProperty("limit").GetInt32();
                if (limit < 0)
                    throw new FormatException("Slot limit must be non-negative");

                return Resolve(expression.GetProperty("slot_source"), source).Take(limit)
                    .Select(slot => slot with { Limit = Math.Min(slot.Limit, limit) }).ToList();

            case "minecraft:filtered":
                var filter = expression.GetProperty("item_filter");
                if (filter.EnumerateObject().Any(property => property.Name is not ("items" or "count")))
                    throw new FormatException("Unsupported slot item predicate");

                return Resolve(expression.GetProperty("slot_source"), source)
                    .Where(slot => Matches(slot.Get(), filter)).ToList();

            case "minecraft:contents":
                var component = CommandInput.Qualify(expression.GetProperty("component").GetString()!);
                var componentType = component switch
                {
                    "minecraft:container" => DataComponentType.Container,
                    "minecraft:bundle_contents" => DataComponentType.BundleContents,
                    "minecraft:charged_projectiles" => DataComponentType.ChargedProjectiles,
                    _ => throw new FormatException($"Unsupported contents component {component}")
                };
                return Resolve(expression.GetProperty("slot_source"), source)
                    .SelectMany(slot => Contents(slot, componentType)).ToList();

            default:
                throw new FormatException($"Unsupported slot source {type}");
        }
    }

    private static IEnumerable<Slot> Contents(Slot parent, DataComponentType type)
    {
        if (parent.Get() is not { IsAir: false })
            yield break;

        if (type != DataComponentType.Container)
        {
            foreach (var slot in GrowableSlots(parent, type))
                yield return slot;

            yield break;
        }

        var component = parent.Get()!.GetComponent<SimpleDataComponent<ImmutableArray<ItemStack>>>(type);
        var count = component?.Value.Length ?? 0;
        var capacity = type == DataComponentType.Container ? ContainerBlockEntity.MaxSlots : count;
        for (var index = 0; index < capacity; index++)
        {
            var captured = index;
            yield return new Slot(parent.Owner,
                () => parent.Get()?.GetComponent<SimpleDataComponent<ImmutableArray<ItemStack>>>(type)
                    ?.Value.ElementAtOrDefault(captured),
                item =>
                {
                    if (parent.Get() is not { } current)
                        return false;

                    var contents = current.GetComponent<SimpleDataComponent<ImmutableArray<ItemStack>>>(type);
                    contents ??= type switch
                    {
                        DataComponentType.Container => ComponentBuilder.Container,
                        DataComponentType.BundleContents => ComponentBuilder.BundleContents,
                        _ => ComponentBuilder.ChargedProjectiles
                    };

                    var items = contents.Value.ToList();
                    while (items.Count <= captured)
                        items.Add(ItemStack.Air);

                    items[captured] = item ?? ItemStack.Air;
                    if (type == DataComponentType.Container)
                    {
                        while (items.Count > 0 && items[^1].IsAir)
                            items.RemoveAt(items.Count - 1);
                    }

                    var copy = new ItemStack(current, current.Count);
                    copy[type] = contents with { Value = [.. items] };
                    return parent.Set(copy);
                }, parent.Sync, index < count, parent.Limit);
        }
    }

    // The entries of a bundle or charged projectiles, then one invisible slot that appends while there's room.
    private static IEnumerable<Slot> GrowableSlots(Slot parent, DataComponentType type)
    {
        var contents = new GrowableContents(parent, type);
        for (var index = 0; index < contents.Count; index++)
        {
            var captured = index;
            yield return new Slot(parent.Owner, () => contents.Get(captured), item => contents.Set(captured, item),
                parent.Sync, true, parent.Limit);
        }

        yield return new Slot(parent.Owner, () => null, _ => false, parent.Sync, false, parent.Limit)
        {
            Growth = contents
        };
    }

    /// <summary>
    /// Vanilla's <c>GrowableMutableContainer</c> for bundle contents and charged projectiles: entries are replaced in
    /// place and new ones appended at the end while there's room, then written back with empty entries removed (and a
    /// bundle's identical stacks merged). Indices stay put until the command is done with the stack.
    /// </summary>
    internal sealed class GrowableContents
    {
        private readonly Slot parent;
        private readonly DataComponentType type;
        private readonly List<ItemStack> items;

        public GrowableContents(Slot parent, DataComponentType type)
        {
            this.parent = parent;
            this.type = type;
            var stored = parent.Get()?.GetComponent<SimpleDataComponent<ImmutableArray<ItemStack>>>(type);
            this.items = [.. stored?.Value ?? []];
        }

        public int Count => this.items.Count;

        /// <summary>Vanilla's <c>canInsertNewSlots</c>: a bundle with room left, or fewer than 1024 projectiles.</summary>
        public bool CanInsertNewSlots => this.IsBundle
            ? this.Weight(-1).Subtract(Bundles.Weight.One).Numerator < 0
            : this.items.Count < MaxChargedProjectiles;

        private bool IsBundle => this.type == DataComponentType.BundleContents;

        public ItemStack Get(int index) => this.items[index];

        /// <summary>Vanilla's <c>setItem</c>: a bundle refuses a stack that doesn't fit or would overfill it.</summary>
        public bool Set(int index, ItemStack? item)
        {
            var replacement = item ?? ItemStack.Air;
            if (this.IsBundle && !replacement.IsAir && !this.Fits(index, replacement))
                return false;

            this.items[index] = new ItemStack(replacement, replacement.Count);
            return this.Commit();
        }

        /// <summary>Vanilla's <c>addSlotWithItem</c> for a non-empty stack.</summary>
        public bool Append(ItemStack item)
        {
            if (this.IsBundle && !this.Fits(-1, item))
                return false;

            this.items.Add(new ItemStack(item, item.Count));
            return this.Commit();
        }

        private bool Fits(int replacedIndex, ItemStack item)
        {
            if (!ItemOwners.FitsInContainerItems(item))
                return false;

            var stacked = Bundles.ItemWeight(item).Times(item.Count);
            var total = this.Weight(replacedIndex).Add(stacked);
            return total.Subtract(Bundles.Weight.One).Numerator <= 0;
        }

        // The bundle's weight, leaving out the entry at excludedIndex.
        private Bundles.Weight Weight(int excludedIndex)
        {
            var total = Bundles.Weight.Zero;
            for (var index = 0; index < this.items.Count; index++)
            {
                var stack = this.items[index];
                if (index == excludedIndex || stack.IsAir)
                    continue;

                total = total.Add(Bundles.ItemWeight(stack).Times(stack.Count));
            }

            return total;
        }

        private bool Commit()
        {
            if (this.parent.Get() is not { } current)
                return false;

            var entries = this.items.Where(stack => !stack.IsAir).ToList();
            if (this.IsBundle)
                MergeIdenticalStacks(entries);

            var component = this.IsBundle ? ComponentBuilder.BundleContents : ComponentBuilder.ChargedProjectiles;
            var copy = new ItemStack(current, current.Count);
            copy[this.type] = component with { Value = [.. entries] };
            return this.parent.Set(copy);
        }

        // Vanilla's mergeIdenticalStacks: each stackable entry joins the next identical one after it.
        private static void MergeIdenticalStacks(List<ItemStack> entries)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                var stack = entries[index];
                if (stack.MaxStackSize <= 1)
                    continue;

                var later = entries.FindIndex(index + 1, other => Bundles.SameKind(stack, other));
                if (later < 0)
                    continue;

                entries[later] = new ItemStack(stack, stack.Count + entries[later].Count);
                entries.RemoveAt(index);
                index--;
            }
        }
    }

    private static List<Slot> Range(JsonElement range, Func<int, Slot?> source) => range.EnumerateArray()
        .Select(id => source(id.GetInt32())).OfType<Slot>().ToList();

    internal static bool Matches(ItemStack? stack, string predicate)
    {
        if (predicate == "*")
            return stack is { IsAir: false };

        if (predicate.Contains('[') || predicate.Contains('{'))
            throw new FormatException("Component item predicates are not implemented");

        var tag = predicate.StartsWith('#');
        var identifier = CommandInput.Qualify(tag ? predicate[1..] : predicate);
        if (tag)
        {
            if (!TagsRegistry.TryGet("item", identifier, out var entry))
                throw new FormatException($"Unknown item tag {identifier}");

            return stack is { IsAir: false } && entry.Entries.Contains(stack.Holder.Id);
        }

        if (!ItemsRegistry.TryGet(identifier, out var item))
            throw new FormatException($"Unknown item {identifier}");

        return stack is { IsAir: false } && stack.Is(item);
    }

    private static bool Matches(ItemStack? stack, JsonElement filter)
    {
        if (stack is null || stack.IsAir)
            return false;

        if (filter.TryGetProperty("items", out var items))
        {
            var matches = items.ValueKind == JsonValueKind.Array
                ? items.EnumerateArray().Any(item => Matches(stack, item.GetString()!))
                : Matches(stack, items.GetString()!);
            if (!matches)
                return false;
        }

        if (!filter.TryGetProperty("count", out var count))
            return true;

        if (count.ValueKind == JsonValueKind.Number)
            return stack.Count == count.GetInt32();

        return (!count.TryGetProperty("min", out var min) || stack.Count >= min.GetInt32())
            && (!count.TryGetProperty("max", out var max) || stack.Count <= max.GetInt32());
    }

    private static Slot? EntitySlot(IEntity entity, int id)
    {
        if (entity is IPlayer player)
        {
            if (id is >= 200 and < 227)
            {
                return InventorySlot(player.EnderInventory, id - 200,
                    () => CommandFeedback.SyncInventoryAsync(player), player);
            }

            var slot = id switch
            {
                >= 0 and < 9 => id + 36,
                >= 9 and < 36 => id,
                98 => player.CurrentHeldItemSlot,
                99 => 45,
                >= 100 and <= 103 => 108 - id,
                _ => -1
            };
            return InventorySlot(player.Inventory, slot, () => CommandFeedback.SyncInventoryAsync(player), player);
        }

        if (entity is Mob mob && id is >= 98 and <= 105)
        {
            var equipment = id switch
            {
                98 => EquipmentSlot.MainHand,
                99 => EquipmentSlot.OffHand,
                100 => EquipmentSlot.Boots,
                101 => EquipmentSlot.Leggings,
                102 => EquipmentSlot.Chestplate,
                103 => EquipmentSlot.Helmet,
                104 => EquipmentSlot.Body,
                _ => EquipmentSlot.Saddle
            };
            return new Slot(mob, () => mob.GetEquipment(equipment), stack =>
            {
                mob.SetEquipment(equipment, stack ?? ItemStack.Air);
                return true;
            }, () => default);
        }

        return null;
    }

    private static Slot? InventorySlot(BaseContainer inventory, int slot, Func<ValueTask> sync, object? owner = null)
    {
        if (slot < 0 || slot >= inventory.Size)
            return null;

        return new Slot(owner ?? inventory, () => inventory.GetItem(slot), stack =>
        {
            inventory.SetItem(slot, stack);
            return true;
        }, sync);
    }
}
