using Obsidian.API.Inventory;
using Obsidian.Nbt;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:item")]
public partial class ItemEntity : Entity
{
    private static readonly TimeSpan DropWaitTime = TimeSpan.FromSeconds(.5);

    public ItemStack Item { get; set; }

    public bool CanPickup { get; set; }

    public DateTimeOffset TimeDropped { get; private set; } = DateTimeOffset.UtcNow;

    public ItemEntity() => this.Type = EntityType.Item;

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(8, EntityMetadataType.Slot);
        writer.WriteItemStack(this.Item);
    }

    // Vanilla's default pickup delay is 10 ticks; a stack that can't be picked up yet keeps it.
    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.Set(this.Item.ToNbt("Item"));
        tag.Set(new NbtTag<short>("PickupDelay", (short)(this.CanPickup ? 0 : 10)));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        if (tag.TryGetTag<NbtCompound>("Item", out var item) && item.ItemFromNbt() is ItemStack stack)
            this.Item = stack;

        this.CanPickup = tag.TryGetTag<NbtTag<short>>("PickupDelay", out var delay) && delay.Value == 0;
    }

    public async override ValueTask TickAsync()
    {
        await base.TickAsync();

        if (!CanPickup && DateTimeOffset.UtcNow - this.TimeDropped > DropWaitTime)
            this.CanPickup = true;

        foreach (var ent in this.Level.GetNonPlayerEntitiesInRange(this.Position, 0.5f))
        {
            if (ent is not ItemEntity itemEntity)
                continue;

            if (itemEntity == this)
                continue;

            this.Item += itemEntity.Item;

            await itemEntity.RemoveAsync();//TODO find a better way to removed item entities that merged
        }
    }
}
