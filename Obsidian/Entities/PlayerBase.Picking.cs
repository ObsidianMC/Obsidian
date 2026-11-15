using Obsidian.API.Inventory;
using Obsidian.API.BlockEntities;
using Obsidian.Nbt;
using System.Text.Json;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    internal async ValueTask PickBlockAsync(Vector position, bool includeData)
    {
        if (this.Level is not AbstractLevel level)
            return;

        IBlock block;
        BlockEntity? entity;
        // The tick lock guards block/entity publication; transfer pins the player's level in the same snapshot.
        lock (level.Fluids.TickLock)
        {
            using var ownership = level.TransferLock.Enter();
            var min = (VectorD)position;
            if (!ReferenceEquals(this.Level, level)
                || !this.CanPick(new BoundingBox(min, min + new VectorD(1)), BlockInteractionRange(this) + 1)
                || level.GetLoadedChunk(position.X >> 4, position.Z >> 4) is not Chunk chunk
                || level.IsOutsideBuildHeight(position.Y))
            {
                return;
            }

            lock (chunk.BlockLock)
            {
                block = chunk.GetBlock(position.X, position.Y, position.Z);
                entity = block.HasBlockEntity()
                    ? chunk.GetBlockEntity(position.X, position.Y, position.Z) as BlockEntity : null;
            }
        }

        if (block.IsAir)
            return;

        // Serialization may take inventory locks. Keep it outside block/transfer locks, using only captured identities.
        var data = entity is null ? null : BlockEntityNbt.Save(entity);
        var withData = includeData && this.GameMode == GameMode.Creative;
        var item = await ClonePickedBlockAsync(level, block, entity, data, withData);
        if (item is { IsAir: false, Count: > 0 })
            await this.SelectPickedItemAsync(item);
    }

    internal async ValueTask PickEntityAsync(int entityId)
    {
        if (this.Level is not AbstractLevel level)
            return;

        ItemStack? item;
        using (level.TransferLock.Enter())
        {
            if (!ReferenceEquals(this.Level, level))
                return;

            var entity = FindPickedEntity(level, entityId);
            var member = entity is EnderDragonPart part ? part.Parent : entity;
            var range = this.GetInteractionRange("minecraft:entity_interaction_range",
                this.GameMode == GameMode.Creative ? 2 : 0, 3);
            if (entity is null || member is null
                || !ReferenceEquals(member.Level, level) || !member.IsMemberOf(level)
                || member is ArmorStand { IsRemoved: true } or Mob { IsRemoved: true }
                    or VehicleEntity { IsRemoved: true }
                || !this.CanPick(entity.CurrentBox, range + 3))
            {
                return;
            }

            item = ClonePickedEntity(member);
        }

        if (item is { IsAir: false, Count: > 0 })
            await this.SelectPickedItemAsync(item);
    }

    private static Entity? FindPickedEntity(AbstractLevel level, int entityId)
    {
        foreach (var (_, region) in level.Regions)
        {
            if (region.Entities.TryGetValue(entityId, out var found))
                return found as Entity;

            // Vanilla assigns the dragon's eight part ids immediately after its own id.
            for (var offset = 1; offset <= 8; offset++)
            {
                if (region.Entities.TryGetValue(entityId - offset, out var parent) && parent is EnderDragon dragon)
                    return dragon.Parts.FirstOrDefault(part => part.EntityId == entityId);
            }
        }

        return null;
    }

    private static ItemStack? ClonePickedEntity(Entity entity)
    {
        ItemStack? item = entity switch
        {
            VehicleEntity vehicle => new ItemStack(vehicle.PickItem),
            ItemFrame { Item: { IsAir: false, Count: > 0 } framed } => new ItemStack(framed, framed.Count),
            GlowItemFrame => new ItemStack(ItemsRegistry.GlowItemFrame),
            ItemFrame => new ItemStack(ItemsRegistry.ItemFrame),
            Painting => new ItemStack(ItemsRegistry.Painting),
            ArmorStand => new ItemStack(ItemsRegistry.ArmorStand),
            Cushion cushion => new ItemStack(ItemsRegistry.Get(
                "minecraft:" + ((Dye)cushion.Color).ToString().ToSnakeCase() + "_cushion")),
            _ => null
        };
        var name = entity.Type is not null || entity.Definition is not null ? EntityNbt.TypeId(entity) : string.Empty;
        if (item is null && entity is Mob && ItemsRegistry.TryGet(name + "_spawn_egg", out var egg))
            item = new ItemStack(egg);
        if (item is null && name is "minecraft:end_crystal" or "minecraft:leash_knot")
            item = new ItemStack(name == "minecraft:end_crystal" ? ItemsRegistry.EndCrystal : ItemsRegistry.Lead);

        if (item is not null && entity is ItemFrame frame && Empty(frame.Item)
            && frame.CustomName is ChatMessage customName)
        {
            item[DataComponentType.CustomName] = ComponentBuilder.CustomName with { Value = customName };
        }

        return item;
    }

    private bool CanPick(BoundingBox box, double range)
    {
        var eye = EyePosition(this);
        var x = eye.X - Math.Clamp(eye.X, box.Min.X, box.Max.X);
        var y = eye.Y - Math.Clamp(eye.Y, box.Min.Y, box.Max.Y);
        var z = eye.Z - Math.Clamp(eye.Z, box.Min.Z, box.Max.Z);
        return x * x + y * y + z * z < range * range;
    }

    private async ValueTask SelectPickedItemAsync(ItemStack requested)
    {
        var defaults = PickDefaults(requested.Holder);
        var changes = new List<ClientboundPacket>();
        ClickWindowState state;
        SetHeldSlotPacket held;
        using (await this.Inventory.LockAsync())
        {
            // Vanilla searches hotbar 0..8 before main inventory 9..35, ignoring count and equipment slots.
            var slots = Enumerable.Range(36, 9).Concat(Enumerable.Range(9, 27)).ToArray();
            var matching = slots.FirstOrDefault(slot =>
                this.Inventory.GetItem(slot) is { IsAir: false, Count: > 0 } stack
                && stack.HasSameEffectiveComponents(requested, defaults), -1);
            var changed = new SortedSet<int>();
            if (matching >= 36)
            {
                this.CurrentHeldItemSlot = (short)(matching - 36);
            }
            else if (matching >= 0 || this.GameMode == GameMode.Creative)
            {
                var hotbar = Enumerable.Range(0, 9)
                    .Select(offset => 36 + (this.CurrentHeldItemSlot - 36 + offset) % 9).ToArray();
                var destination = hotbar.FirstOrDefault(slot => Empty(this.Inventory.GetItem(slot)), -1);
                if (destination < 0)
                {
                    destination = hotbar.FirstOrDefault(slot =>
                        this.Inventory.GetItem(slot)!.GetComponent<SimpleDataComponent<ImmutableArray<Enchantment>>>(
                            DataComponentType.Enchantments)?.StoredValue.Length is not > 0, this.CurrentHeldItemSlot);
                }

                var previous = this.Inventory.GetItem(destination);
                this.CurrentHeldItemSlot = (short)(destination - 36);
                if (matching >= 0)
                {
                    this.Inventory.SetItem(destination, this.Inventory.GetItem(matching));
                    this.Inventory.SetItem(matching, previous);
                    changed.Add(matching);
                }
                else
                {
                    var free = slots.FirstOrDefault(slot => Empty(this.Inventory.GetItem(slot)), -1);
                    if (!Empty(previous) && free >= 0)
                    {
                        this.Inventory.SetItem(free, previous);
                        changed.Add(free);
                    }

                    this.Inventory.SetItem(destination, requested);
                }

                changed.Add(destination);
            }

            // Vanilla broadcasts inventory-menu changes in menu slot order, after the selected-slot packet.
            held = new SetHeldSlotPacket((byte)(this.CurrentHeldItemSlot - 36));
            foreach (var slot in changed)
            {
                this.Inventory.StateId = (this.Inventory.StateId + 1) & 32767;
                var stack = this.Inventory.GetItem(slot);
                changes.Add(new ContainerSetSlotPacket
                {
                    ContainerId = 0,
                    StateId = this.Inventory.StateId,
                    Slot = (short)slot,
                    SlotData = stack is null ? null : NetworkBuffer.SnapshotItemStack(stack)
                });
            }

            state = ClickWindowState.Of(this);
        }

        // Uses the same ordered delivery and stale-snapshot recovery as inventory clicks and pickups.
        await this.WindowSnapshots.SendChangeAsync(this, state, changes, lead: held);
    }

    private static readonly Lazy<Dictionary<string, JsonElement>> pickDefaults = new(() =>
    {
        using var stream = typeof(PlayerBase).Assembly.GetManifestResourceStream("Obsidian.Assets.item_components.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateObject().ToDictionary(
            item => item.Name, item => item.Value.GetProperty("components").Clone());
    });

    private static readonly ConcurrentDictionary<string, ItemStack> pickDefaultStacks = new();

    private static ItemStack PickDefaults(Item item)
    {
        if (!pickDefaults.Value.TryGetValue(item.UnlocalizedName, out var components))
            return new ItemStack(item);

        return pickDefaultStacks.GetOrAdd(item.UnlocalizedName, _ => new NbtCompound
        {
            new NbtTag<string>("id", item.UnlocalizedName),
            JsonNbt.ToNbt("components", components)
        }.ItemFromNbt()!);
    }

    private static bool Empty(ItemStack? item) => item is null || item.IsAir || item.Count <= 0;
}
