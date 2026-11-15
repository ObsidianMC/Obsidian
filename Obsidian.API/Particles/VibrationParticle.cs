namespace Obsidian.API.Particles;

/// <summary>A particle that travels from a vibration's source to a sculk sensor.</summary>
public class VibrationParticle : IParticle
{
    /// <summary>
    /// Gets the legacy particle id this class stores. It is not this particle's id in the 26.3 particle
    /// registry, and the current particle-sending API, <see cref="ParticleData"/>, does not use this class.
    /// </summary>
    public int Id => 37;

    /// <summary>Gets or sets where the particle starts.</summary>
    public VectorF Origin { get; set; }

    /// <summary>Gets or sets where the particle travels to.</summary>
    public VectorF Destination { get; set; }

    /// <summary>Gets or sets how long the particle takes to reach <see cref="Destination"/>, in ticks.</summary>
    public int Ticks { get; set; }
}
