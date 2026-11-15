namespace Obsidian.API.Particles;

/// <summary>A dust particle that falls from under a block, like the dust under floating sand.</summary>
public class FallingDustParticle : IParticle
{
    /// <summary>
    /// Gets the legacy particle id this class stores. It is not this particle's id in the 26.3 particle
    /// registry, and the current particle-sending API, <see cref="ParticleData"/>, does not use this class.
    /// </summary>
    public int Id => 25;

    /// <summary>Gets or sets the block state id whose color the dust takes.</summary>
    public int BlockState { get; set; }
}
