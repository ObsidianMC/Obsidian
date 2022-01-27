using Obsidian.Net;
using Obsidian.Utilities.Registry;

namespace Obsidian.Entities;

public class ItemEntity : Entity
{
    public int Id { get; set; }

    public Material Material => Registry.GetItem(Id).Type;

    public sbyte Count { get; set; }

    public ItemMeta ItemMeta { get; set; }

    public bool CanPickup { get; set; }

    public DateTimeOffset TimeDropped { get; private set; } = DateTimeOffset.UtcNow;

    public ItemEntity() => Type = EntityType.Item;

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteEntityMetdata(8, EntityMetadataType.Slot, new ItemStack(Material, Count, ItemMeta));
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteEntityMetadataType(8, EntityMetadataType.Slot);
        stream.WriteItemStack(new ItemStack(Material, Count, ItemMeta));
    }

    public override async Task TickAsync()
    {
        await base.TickAsync();

        if (!CanPickup && TimeDropped.Subtract(DateTimeOffset.UtcNow).TotalSeconds > 5)
            CanPickup = true;

        foreach (var ent in World.GetEntitiesNear(Position, 1.5f))
        {
            if (ent is ItemEntity item)
            {
                if (item == this)
                    continue;

                Count += item.Count;

                await item.RemoveAsync();//TODO find a better way to removed item entities that merged
            }
        }
    }
}
