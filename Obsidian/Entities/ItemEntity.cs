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
            
        }
    }
}
