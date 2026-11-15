namespace Obsidian.API.Particles;

/// <summary>A colored dust particle, like the one redstone dust gives off.</summary>
public class DustParticle : IParticle
{
    /// <summary>Gets the numeric particle type id this class is sent as.</summary>
    public int Id => 15;

    /// <summary>Gets or sets the dust's color and size.</summary>
    public ParticleColor Color { get; set; }
}

/// <summary>The color and size of a dust particle.</summary>
public record struct ParticleColor
{
    /// <summary>Gets or sets the red component.</summary>
    public float R { get; set; }

    /// <summary>Gets or sets the green component.</summary>
    public float G { get; set; }

    /// <summary>Gets or sets the blue component.</summary>
    public float B { get; set; }

    /// <summary>Gets or sets the particle's size multiplier.</summary>
    public float Scale { get; set; }

    /// <summary>Gets or sets whether <see cref="Scale"/> is ignored.</summary>
    public bool IgnoreScale { get; set; }
}
