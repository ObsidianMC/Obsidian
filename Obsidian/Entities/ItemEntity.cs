using Obsidian.Items;
using Obsidian.Net;
using Obsidian.Util.DataTypes;
using System;
using System.Linq;
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

            await stream.WriteEntityMetdata(7, EntityMetadataType.Slot, new Slot
            {
                Present = true,
                Id = this.Id,
                Count = this.Count,
                ItemNbt = this.Nbt
            });
        }

        public override async Task TickAsync()
        {
            foreach (var ent in this.World.GetEntitiesNear(this.Location, 5))
            {
                if (ent is ItemEntity item)
                {
                    if (item == this)
                        continue;

                    if (Position.DistanceTo(this.Location, item.Location) <= 1.5)
                    {
                        this.Count += item.Count;
                        _ = item.RemoveAsync();//TODO call entity merge event
                    }

                }
            }
        }
    }
}
