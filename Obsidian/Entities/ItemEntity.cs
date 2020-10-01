using Obsidian.Items;
using Obsidian.Net;
using System.Threading.Tasks;

namespace Obsidian.Entities
{
    public class ItemEntity : Entity
    {
        public int Id { get; set; }

        public sbyte Count { get; set; }

        public ItemNbt Nbt { get; set; }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

            await stream.WriteEntityMetdata(7, EntityMetadataType.Slot, new ItemStack
            {
                Present = true,
                Id = this.Id,
                Count = this.Count,
                Nbt = this.Nbt
            });
        }

        public override Task TickAsync()
        {
            foreach (var ent in this.World.GetEntitiesNear(this.Location, .5))
            {
                if (ent is ItemEntity item)
                {
                    if (item == this)
                        continue;

                    this.Count += item.Count;

                    _ = item.RemoveAsync();//TODO find a better way to removed item entities that merged
                }
            }

            return Task.CompletedTask;
        }
    }
}
