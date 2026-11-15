namespace Obsidian.API.Particles;

/// <summary>A particle that shows a block's texture, such as the bits that fly off a block being broken.</summary>
public class BlockParticle : IParticle
{
    /// <summary>
    /// Gets the legacy particle id this class stores. It is not this particle's id in the 26.3 particle
    /// registry, and the current particle-sending API, <see cref="ParticleData"/>, does not use this class.
    /// </summary>
    public int Id => 4;

    /// <summary>Gets or sets the block state id whose texture the particle shows.</summary>
    public int BlockState { get; set; }
}
