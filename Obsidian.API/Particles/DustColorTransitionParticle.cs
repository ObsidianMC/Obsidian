namespace Obsidian.API.Particles;

/// <summary>A dust particle that fades from one color to another.</summary>
public class DustColorTransitionParticle : IParticle
{
    /// <summary>
    /// Gets the legacy particle id this class stores. It is not this particle's id in the 26.3 particle
    /// registry, and the current particle-sending API, <see cref="ParticleData"/>, does not use this class.
    /// </summary>
    public int Id => 16;

    /// <summary>Gets or sets the starting color.</summary>
    public ParticleColor From { get; set; }

    /// <summary>Gets or sets the color the particle fades to.</summary>
    public ParticleColor To { get; set; }
}
