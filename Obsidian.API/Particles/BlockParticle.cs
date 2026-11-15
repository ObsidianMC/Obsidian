namespace Obsidian.API.Particles;

/// <summary>A particle that shows a block's texture, such as the bits that fly off a block being broken.</summary>
public class BlockParticle : IParticle
{
    /// <summary>Gets the numeric particle type id this class is sent as.</summary>
    public int Id => 4;

    /// <summary>Gets or sets the block state id whose texture the particle shows.</summary>
    public int BlockState { get; set; }
}
