namespace Obsidian.API.Particles
{
    public class DustColorTransitionParticle : IParticle
    {
        public int Id => 16;

        public ParticleColor From { get; set; }

        public ParticleColor To {  get; set; }
    }
}
