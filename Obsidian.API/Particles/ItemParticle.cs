using Obsidian.API.Inventory;

namespace Obsidian.API.Particles;

public class ItemParticle : IParticle
{
    public ItemParticle(ItemStack item)
    {
        Item = item;
    }

    public int Id => 36;

    public ItemStack Item { get; set; }
}
