namespace Obsidian.API.Particles;

public class DustParticle : IParticle
{
    public int Id => 15;

    public ParticleColor Color { get; set; }
}

public struct ParticleColor
{
    public float R { get; set; }

    public float G { get; set; }

    public float B { get; set; }

    public float Scale { get; set; }

    public bool IgnoreScale { get; set; }
}
