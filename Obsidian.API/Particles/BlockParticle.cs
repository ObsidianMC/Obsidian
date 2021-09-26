namespace Obsidian.API.Particles
{
    public class BlockParticle : IParticle
    {
        public int Id => 4;

        public int BlockState { get; set; }
    }
}
