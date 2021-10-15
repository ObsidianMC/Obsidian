namespace Obsidian.API.Particles
{
    public class ItemParticle : IParticle
    {
        public int Id => 36;

        public ItemStack Item { get; set; }
    }
}
