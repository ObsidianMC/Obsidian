using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Nbt;

namespace Obsidian.WorldData;

/// <summary>
/// Block entities' item components, like vanilla's <c>BlockEntity.collectComponents</c> and
/// <c>applyComponentsFromItemStack</c>: what a broken block entity gives its block's loot (the components
/// <c>copy_components</c> copies, and the items of <c>dynamic</c> entries), and what a placed block item gives its
/// block entity back.
/// </summary>
/// <remarks>
/// Covers the components of the types the server models: containers' <c>custom_name</c>, <c>lock</c>,
/// <c>container_loot</c> and a shulker box's <c>container</c> (and its <c>contents</c> dynamic drop); decorated pots'
/// <c>pot_decorations</c> (and their <c>sherds</c> dynamic drop); and the <c>custom_name</c> of other named block
/// entities kept as data. Banner patterns, bees and skull profiles aren't modelled yet.
/// </remarks>
internal sealed class BlockEntityComponents
{
    /// <summary>
    /// Nothing for loot, for blocks without a block entity.
    /// </summary>
    public static readonly BlockEntityComponents None = new([], []);

    // Vanilla's PotDecorations: the back, left, right and front faces, bricks where they have no sherd.
    private const int PotFaces = 4;

    private readonly Dictionary<string, List<ItemStack>> dynamic;

    private BlockEntityComponents(List<DataComponent> components, Dictionary<string, List<ItemStack>> dynamic)
    {
        this.Components = components;
        this.dynamic = dynamic;
    }

    /// <summary>
    /// The components for <c>copy_components</c> with the <c>block_entity</c> source.
    /// </summary>
    public IReadOnlyList<DataComponent> Components { get; }

    /// <summary>
    /// The items of the <c>dynamic</c> entries the block names, copied for each roll, or null when it names none.
    /// </summary>
    public Action<string, Action<ItemStack>>? DynamicDrops => this.dynamic.Count == 0 ? null : this.Drop;

    /// <summary>
    /// Whether the components hold items in a <c>container</c>: a filled shulker box.
    /// </summary>
    public bool HoldsItems => this.Components.Any(component =>
        component is SimpleDataComponent<ImmutableArray<ItemStack>> { Type: DataComponentType.Container } container
        && container.Value.Any(item => !item.IsAir && item.Count > 0));

    /// <summary>
    /// Takes what the block entity at <paramref name="position"/> gives its block's loot, before the block is removed.
    /// </summary>
    /// <remarks>
    /// A shulker box's contents go into the components, so it's retired in the same step, under its lock: no click
    /// takes an item the box item holds, and its own removal then drops nothing. Like vanilla's
    /// <c>ShulkerBoxBlock.playerWillDestroy</c>, a player breaking one rolls the loot it was waiting for into it first;
    /// an explosion copies the loot table instead.
    /// </remarks>
    /// <param name="level">The level.</param>
    /// <param name="position">The block's position.</param>
    /// <param name="breaker">The player breaking the block, if one is.</param>
    public static async ValueTask<BlockEntityComponents> CaptureAsync(ILevel level, Vector position, IPlayer? breaker)
    {
        if (level is not AbstractLevel concrete)
            return None;

        return await level.GetBlockEntityAsync(position.X, position.Y, position.Z) switch
        {
            ContainerBlockEntity container => await CaptureContainerAsync(concrete, container, breaker),
            DataBlockEntity data => CaptureData(data),
            _ => None
        };
    }

    /// <summary>
    /// Gives the block entity at <paramref name="position"/> what the block item placed there holds, like vanilla's
    /// <c>applyImplicitComponents</c>: a container's name, items, lock and loot table.
    /// </summary>
    public static async ValueTask ApplyAsync(ILevel level, Vector position, ItemStack item)
    {
        if (await level.GetBlockEntityAsync(position.X, position.Y, position.Z) is not ContainerBlockEntity container)
            return;

        var inventory = container.Inventory;
        using (await inventory.LockAsync())
        lock (container.LootLock)
        {
            var customName = item.GetComponent<SimpleDataComponent<ChatMessage>>(DataComponentType.CustomName);
            if (customName?.Value is ChatMessage name)
            {
                inventory.CustomName = name;
                inventory.Title = name;
            }

            var contents = item.GetComponent<SimpleDataComponent<ImmutableArray<ItemStack>>>(
                DataComponentType.Container);
            if (contents is not null)
            {
                for (var slot = 0; slot < inventory.Size && slot < contents.Value.Length; slot++)
                {
                    var stack = contents.Value[slot];
                    inventory.SetItem(slot, stack.IsAir || stack.Count <= 0 ? null : new ItemStack(stack, stack.Count));
                }
            }

            if (item.GetComponent<SimpleDataComponent<NbtCompound>>(DataComponentType.Lock)?.Value is NbtCompound key
                && container is VanillaContainerBlockEntity vanilla)
            {
                vanilla.LockPredicate = key;
            }

            var loot = item.GetComponent<SimpleDataComponent<NbtCompound>>(DataComponentType.ContainerLoot)?.Value;
            if (loot is not null && loot.TryGetTagValue<string>("loot_table", out var table))
            {
                container.LootTable = table;
                container.LootTableSeed = loot.TryGetTagValue<long>("seed", out var seed) ? seed : 0;
            }
        }
    }

    // Vanilla's BaseContainerBlockEntity and RandomizableContainerBlockEntity collectImplicitComponents.
    private static async ValueTask<BlockEntityComponents> CaptureContainerAsync(
        AbstractLevel level,
        ContainerBlockEntity container,
        IPlayer? breaker)
    {
        var components = new List<DataComponent>();
        var dynamic = new Dictionary<string, List<ItemStack>>();
        var inventory = container.Inventory;

        using (await inventory.LockAsync())
        {
            // Its contents drop with the block's loot, once: a second break of the same box gets none.
            if (!container.DropsContents && !inventory.TryRetire())
                return None;

            if (!container.DropsContents && breaker is not null)
                level.UnpackLootTable(container, breaker.GameMode == GameMode.Creative ? null : breaker);

            if (inventory.CustomName is ChatMessage name)
                components.Add(ComponentBuilder.CustomName with { Value = name });

            if (container is VanillaContainerBlockEntity { LockPredicate: NbtCompound key })
                components.Add(ComponentBuilder.Lock with { Value = key });

            // A container whose items drop as it goes (a chest) has dropped them by the time vanilla rolls its loot,
            // so only one that keeps them gives them.
            if (!container.DropsContents)
            {
                var items = Contents(inventory);
                components.Add(ComponentBuilder.Container with { Value = items });
                dynamic["minecraft:contents"] = [.. items.Where(item => !item.IsAir && item.Count > 0)];
            }

            lock (container.LootLock)
            {
                if (container.LootTable is string lootTable)
                {
                    var loot = new NbtCompound { new NbtTag<string>("loot_table", lootTable) };
                    if (container.LootTableSeed != 0)
                        loot.Add(new NbtTag<long>("seed", container.LootTableSeed));

                    components.Add(ComponentBuilder.ContainerLoot with { Value = loot });
                }
            }
        }

        return new BlockEntityComponents(components, dynamic);
    }

    // Vanilla's ItemContainerContents.fromItems: copies of the slots up to the last that holds something.
    private static ImmutableArray<ItemStack> Contents(BaseContainer inventory)
    {
        var last = -1;
        for (var slot = 0; slot < inventory.Size; slot++)
        {
            if (inventory[slot] is { IsAir: false, Count: > 0 })
                last = slot;
        }

        var items = new ItemStack[last + 1];
        for (var slot = 0; slot <= last; slot++)
        {
            items[slot] = inventory[slot] is { IsAir: false, Count: > 0 } item
                ? new ItemStack(item, item.Count)
                : new ItemStack(ItemsRegistry.Air, 0);
        }

        return [.. items];
    }

    // The components of the types kept as data: vanilla's DecoratedPotBlockEntity's decorations and
    // DecoratedPotBlock's sherds dynamic drop, and the names of nameable ones.
    private static BlockEntityComponents CaptureData(DataBlockEntity blockEntity)
    {
        var data = blockEntity.SnapshotData();
        var components = new List<DataComponent>();
        var dynamic = new Dictionary<string, List<ItemStack>>();

        if (data.TryGetTag("CustomName", out var customName) && customName.TextFromNbt() is ChatMessage name)
            components.Add(ComponentBuilder.CustomName with { Value = name });

        if (blockEntity.Id == "minecraft:decorated_pot")
        {
            var sherds = data.TryGetTag<NbtList>("sherds", out var list)
                ? list.OfType<NbtTag<string>>().Select(sherd => ItemsRegistry.Get(sherd.Value!)).ToList()
                : [];

            var faces = Enumerable.Range(0, PotFaces)
                .Select(face => face < sherds.Count && sherds[face] != ItemsRegistry.Air
                    ? sherds[face]
                    : ItemsRegistry.Brick)
                .ToImmutableArray();

            components.Add(ComponentBuilder.PotDecorations with { Value = faces });
            dynamic["minecraft:sherds"] = [.. faces.Select(face => new ItemStack(face))];
        }

        return components.Count == 0 && dynamic.Count == 0 ? None : new BlockEntityComponents(components, dynamic);
    }

    private void Drop(string name, Action<ItemStack> output)
    {
        if (!this.dynamic.TryGetValue(name, out var items))
            return;

        foreach (var item in items)
            output(new ItemStack(item, item.Count));
    }
}
