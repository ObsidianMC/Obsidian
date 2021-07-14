using Obsidian.API;
using Obsidian.Net;
using Obsidian.Utilities.Registry;
using System;
using System.Threading.Tasks;

namespace Obsidian.Entities
{
    public class ItemEntity : Entity
    {
        public int Id { get; set; }

        public Material Material => Registry.GetItem(this.Id).Type;

        public sbyte Count { get; set; }

        public ItemMeta ItemMeta { get; set; }

        public bool CanPickup { get; set; }

        public DateTimeOffset TimeDropped { get; private set; } = DateTimeOffset.UtcNow;

        public ItemEntity() => this.Type = EntityType.Item;

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

            await stream.WriteEntityMetdata(7, EntityMetadataType.Slot, new ItemStack(this.Material, this.Count, this.ItemMeta));
        }

        public override void Write(MinecraftStream stream)
        {
            base.Write(stream);

            stream.WriteEntityMetadataType(7, EntityMetadataType.Slot);
            stream.WriteItemStack(new ItemStack(this.Material, this.Count, this.ItemMeta));
        }

        public override async Task TickAsync()
        {
            await base.TickAsync();

            if (!CanPickup && this.TimeDropped.Subtract(DateTimeOffset.UtcNow).TotalSeconds > 5)
                this.CanPickup = true;

            foreach (var ent in this.World.GetEntitiesNear(this.Position, 1.5f))
            {
                if (ent is ItemEntity item)
                {
                    if (item == this)
                        continue;

                    this.Count += item.Count;

                    await item.RemoveAsync();//TODO find a better way to removed item entities that merged
                }
            }
        }
    }
}
