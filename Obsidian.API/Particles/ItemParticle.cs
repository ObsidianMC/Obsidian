using Obsidian.API.Inventory;

namespace Obsidian.API.Particles;

/// <summary>A particle that shows an item's texture, such as the crumbs of food being eaten.</summary>
public class ItemParticle : IParticle
{
    /// <summary>Creates a particle that shows <paramref name="item"/>.</summary>
    /// <param name="item">The item whose texture the particle shows.</param>
    public ItemParticle(ItemStack item)
    {
        Item = item;
    }

    /// <summary>
    /// Gets the legacy particle id this class stores. It is not this particle's id in the 26.3 particle
    /// registry, and the current particle-sending API, <see cref="ParticleData"/>, does not use this class.
    /// </summary>
    public int Id => 36;

    /// <summary>Gets or sets the item whose texture the particle shows.</summary>
    public ItemStack Item { get; set; }
}
