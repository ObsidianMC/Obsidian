using Obsidian.Registries;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:item")]
public partial class ItemEntity : Entity
{
    private static readonly TimeSpan DropWaitTime = TimeSpan.FromSeconds(3);

    public int Id { get; set; }

    public Material Material => ItemsRegistry.Get(this.Id).Type;

    public sbyte Count { get; set; }

    public ItemMeta ItemMeta { get; set; }

    public bool CanPickup { get; set; }

    public DateTimeOffset TimeDropped { get; private set; } = DateTimeOffset.UtcNow;

    public ItemEntity() => this.Type = EntityType.Item;


    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(8, EntityMetadataType.Slot);
        writer.WriteItemStack(new ItemStack(this.Material, this.Count, this.ItemMeta));
    }

    public async override ValueTask TickAsync()
    {
        await base.TickAsync();

        if (!CanPickup && DateTimeOffset.UtcNow - this.TimeDropped > DropWaitTime)
            this.CanPickup = true;

        foreach (var ent in this.world.GetNonPlayerEntitiesInRange(this.Position, 0.5f))
        {
            if (ent is not ItemEntity item)
                continue;

            if (item == this)
                continue;

            this.Count += item.Count;

            await item.RemoveAsync();//TODO find a better way to removed item entities that merged
        }
    }
}
