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
    internal sealed record Slot(object Owner, Func<ItemStack?> Get, Action<ItemStack?> Set, Func<ValueTask> Sync,
        bool Visible = true, int Limit = int.MaxValue);

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
                        return;

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
                    parent.Set(copy);
                }, parent.Sync, index < count, parent.Limit);
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
            return new Slot(mob, () => mob.GetEquipment(equipment),
                stack => mob.SetEquipment(equipment, stack ?? ItemStack.Air), () => default);
        }

        return null;
    }

    private static Slot? InventorySlot(BaseContainer inventory, int slot, Func<ValueTask> sync, object? owner = null) =>
        slot < 0 || slot >= inventory.Size ? null : new Slot(owner ?? inventory,
            () => inventory.GetItem(slot), stack => inventory.SetItem(slot, stack), sync);
}
