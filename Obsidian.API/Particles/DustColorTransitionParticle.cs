namespace Obsidian.API.Particles;

/// <summary>A dust particle that fades from one color to another.</summary>
public class DustColorTransitionParticle : IParticle
{
    /// <summary>Gets the numeric particle type id this class is sent as.</summary>
    public int Id => 16;

    /// <summary>Gets or sets the starting color.</summary>
    public ParticleColor From { get; set; }

    /// <summary>Gets or sets the color the particle fades to.</summary>
    public ParticleColor To { get; set; }
}
