namespace Obsidian.API.Particles;

/// <summary>A dust particle that falls from under a block, like the dust under floating sand.</summary>
public class FallingDustParticle : IParticle
{
    /// <summary>Gets the numeric particle type id this class is sent as.</summary>
    public int Id => 25;

    /// <summary>Gets or sets the block state id whose color the dust takes.</summary>
    public int BlockState { get; set; }
}
